using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Owns the dedicated background STA thread, its Dispatcher pump and the
// message-only window that receives WM_CLIPBOARDUPDATE. The pump and window
// stay alive from construction to Dispose so WriteTextAsync keeps working while
// capture is stopped; Start/Stop only toggle the clipboard listener.
internal sealed class ClipboardMonitorService : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly nint HwndMessage = new(-3);

    private readonly Action<ClipboardCapture> _onCapture;
    private readonly Func<ClipboardCaptureOptions> _readOptions;
    private readonly IClipboardNative _native = new Win32ClipboardNative();
    private readonly ClipboardWriteSuppression _suppression = new();
    private readonly ClipboardWriter _writer;
    private readonly Thread _thread;
    private readonly TaskCompletionSource _pumpReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Dispatcher? _dispatcher;
    private HwndSource? _source;
    private bool _listening;
    private bool _disposed;

    public ClipboardMonitorService(Action<ClipboardCapture> onCapture, Func<ClipboardCaptureOptions> readOptions)
    {
        _onCapture = onCapture;
        _readOptions = readOptions;
        _writer = new ClipboardWriter(_native, _suppression, () => _dispatcher, () => _pumpReady.Task);
        _thread = new Thread(RunPump) { IsBackground = true, Name = "Lertaro.ClipboardHistory.Monitor" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Start()
    {
        if (_disposed) return;
        InvokeOnPump(() =>
        {
            if (_listening) return;
            if (_source == null)
            {
                // ParentWindow = HWND_MESSAGE turns this into a message-only
                // window: never visible, still receives clipboard updates.
                var parameters = new HwndSourceParameters("Lertaro.ClipboardHistory.Monitor")
                {
                    ParentWindow = HwndMessage,
                    WindowStyle = 0,
                    ExtendedWindowStyle = 0,
                    Width = 0,
                    Height = 0,
                };
                var source = new HwndSource(parameters);
                source.AddHook(WndProc);
                _source = source;
            }
            // Re-attach on every Start so a Stop/Start cycle re-enables capture.
            if (_native.AddClipboardFormatListener(_source.Handle))
            {
                _listening = true;
            }
            else
            {
                LogFailure(new Win32Exception(Marshal.GetLastWin32Error()), "AddClipboardFormatListener");
            }
        });
    }

    public void Stop()
    {
        if (_disposed) return;
        InvokeOnPump(() =>
        {
            if (!_listening) return;
            if (!_native.RemoveClipboardFormatListener(_source!.Handle))
                LogFailure(new Win32Exception(Marshal.GetLastWin32Error()), "RemoveClipboardFormatListener");
            _listening = false;
            // The window, hook and pump stay alive so WriteTextAsync keeps
            // working and a later Start can re-enable capture.
        });
    }

    // Write-backs live in ClipboardWriter, which owns the suppression scope and the STA pump
    // invocation; the monitor forwards so callers keep one surface.
    public Task WriteTextAsync(string text) => _writer.WriteTextAsync(text);
    public Task WriteFilesAsync(IReadOnlyList<string> paths) => _writer.WriteFilesAsync(paths);
    public Task WriteImageAsync(string imageFilePath) => _writer.WriteImageAsync(imageFilePath);
    public Task WriteTextWithHtmlAsync(string text, string html) => _writer.WriteTextWithHtmlAsync(text, html);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InvokeOnPump(() =>
        {
            if (_source != null)
            {
                if (_listening) _native.RemoveClipboardFormatListener(_source.Handle);
                _source.RemoveHook(WndProc);
                _source.Dispose(); // destroys the message-only window
                _source = null;
            }
            _listening = false;
        });
        _dispatcher?.InvokeShutdown();
        _thread.Join();
    }

    private void RunPump()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _pumpReady.SetResult();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            _pumpReady.TrySetException(ex);
            LogFailure(ex, "message pump");
        }
    }

    private void InvokeOnPump(Action action)
    {
        try
        {
            _pumpReady.Task.GetAwaiter().GetResult();
            _dispatcher!.Invoke(action);
        }
        catch (Exception ex) { LogFailure(ex, "listener lifecycle"); }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmClipboardUpdate) return nint.Zero;
        handled = true;
        HandleClipboardUpdate();
        return nint.Zero;
    }

    private void HandleClipboardUpdate()
    {
        try
        {
            var options = _readOptions();
            if (!options.Enabled || !_listening) return;

            var sequence = _native.GetClipboardSequenceNumber();
            if (_suppression.ShouldSkipCapture(sequence)) return;

            var owner = _native.GetClipboardOwner();
            if (owner == nint.Zero) return; // no reliable owner: skip conservatively

            _native.GetWindowThreadProcessId(owner, out var processId);
            var identity = ResolveProcessIdentity(processId);
            if (identity == null) return; // unreadable identity: skip conservatively
            if (ClipboardExclusionList.IsExcluded(options.ExcludedProcesses, identity.Value.ProcessName)) return;

            // One update yields exactly one capture, tried in the order users mean it: a text copy
            // that also carries a bitmap (Excel, browsers) is a text copy; a bare drop is files; a
            // bare bitmap is an image.
            var capture = ReadTextCapture(options, sequence, owner, identity.Value, processId)
                ?? ReadFileCapture(sequence, owner, identity.Value, processId)
                ?? ReadImageCapture(sequence, owner, identity.Value, processId);
            if (capture == null) return;

            // Re-check after the (possibly slow) read so a disable that happens
            // while an update is in flight still prevents the callback.
            if (!_readOptions().Enabled) return;
            if (!IsStillClipboardOwner(owner, processId)) return;
            _onCapture(capture);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "capture");
        }
    }

    private ClipboardCapture? ReadTextCapture(ClipboardCaptureOptions options, uint sequence, nint owner,
        ProcessIdentity identity, int processId)
    {
        var text = ReadSnapshot(options.MaxTextLength, sequence, owner);
        if (string.IsNullOrEmpty(text)) return null;

        // HTML rides along when the source offers it; its absence or a size cap never blocks the
        // text capture. A changed clipboard under the HTML read only drops the flavor, not the row.
        var html = _source == null ? null : new ClipboardHtmlReader(_native, _source.Handle).Read(sequence, owner);
        return ClipboardCapture.FromText(text, identity, processId, identity.MainWindowHandle, html ?? string.Empty);
    }

    private ClipboardCapture? ReadFileCapture(uint sequence, nint owner, ProcessIdentity identity, int processId)
    {
        if (_source == null || !_native.IsClipboardFormatAvailable(IClipboardNative.CfHDrop)) return null;
        var paths = new ClipboardFileReader(_native, _source.Handle).Read(sequence, owner);
        return paths is not { Count: > 0 } ? null : ClipboardCapture.FromFiles(paths, identity, processId, identity.MainWindowHandle);
    }

    private ClipboardCapture? ReadImageCapture(uint sequence, nint owner, ProcessIdentity identity, int processId)
    {
        var hasBitmap = _native.IsClipboardFormatAvailable(IClipboardNative.CfBitmap) ||
                        _native.IsClipboardFormatAvailable(IClipboardNative.CfDib) ||
                        _native.IsClipboardFormatAvailable(IClipboardNative.CfDibV5);
        if (!hasBitmap) return null;
        var payload = new ClipboardImageReader(_native).Read(sequence, owner);
        return payload == null ? null : ClipboardCapture.FromImage(payload.Png, payload.Width, payload.Height, identity, processId, identity.MainWindowHandle);
    }

    private string? ReadSnapshot(int maxTextLength, uint sequence, nint owner)
    {
        if (_source == null) return null;
        // Open with our own message window (never the external owner window) and
        // let the reader validate the sequence/owner snapshot inside the open.
        return new ClipboardTextReader(_native, _source.Handle).Read(maxTextLength, sequence, owner);
    }

    private bool IsStillClipboardOwner(nint owner, int processId)
    {
        var ownerNow = _native.GetClipboardOwner();
        if (ownerNow != owner) return false;
        _native.GetWindowThreadProcessId(ownerNow, out var processIdNow);
        return processIdNow == processId;
    }

    private static ProcessIdentity? ResolveProcessIdentity(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessIdentity(
                process.ProcessName,
                process.MainWindowTitle ?? string.Empty,
                process.StartTime.ToUniversalTime().Ticks,
                process.MainWindowHandle);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            LogFailure(ex, "resolve owner process");
            return null;
        }
    }

    // Logs contain no clipboard text or window titles: only the failure site,
    // exception type and HRESULT, matching the runtime's scrubbed log style.
    private static void LogFailure(Exception ex, string site) =>
        Logger.Log($"[ClipboardHistory] {site}: {ex.GetType().Name} (0x{ex.HResult:X8}).", LogLevel.Warn);
}

internal readonly record struct ProcessIdentity(string ProcessName, string WindowTitle, long StartedAtUtcTicks, nint MainWindowHandle);
