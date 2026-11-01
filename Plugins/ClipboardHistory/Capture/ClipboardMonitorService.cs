using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
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

    // Runs on the STA pump. The exception from a busy clipboard propagates to
    // the awaiting caller, which owns user-facing error handling.
    public Task WriteTextAsync(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pumpReady.Task.GetAwaiter().GetResult();
        return _dispatcher!.InvokeAsync(() =>
        {
            var before = _native.GetClipboardSequenceNumber();
            // Scoped suppression covers the notifications WPF raises reentrantly
            // inside SetDataObject; EndWrite runs even when a busy clipboard
            // throws so the scope can never stay latched.
            _suppression.BeginWrite();
            try
            {
                Clipboard.SetDataObject(text, true);
            }
            finally
            {
                var after = _native.GetClipboardSequenceNumber();
                _native.GetWindowThreadProcessId(_native.GetClipboardOwner(), out var ownerProcess);
                if (ownerProcess == Environment.ProcessId) _suppression.EndWrite(before, after);
                else _suppression.Clear();
            }
        }).Task;
    }

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

            var text = ReadSnapshot(options.MaxTextLength, sequence, owner);
            if (string.IsNullOrEmpty(text)) return;

            // Re-check after the (possibly slow) read so a disable that happens
            // while an update is in flight still prevents the callback.
            if (!_readOptions().Enabled) return;
            if (!IsStillClipboardOwner(owner, processId)) return;
            _onCapture(new ClipboardCapture(
                text, identity.Value.ProcessName, identity.Value.WindowTitle,
                processId, identity.Value.StartedAtUtcTicks, identity.Value.MainWindowHandle));
        }
        catch (Exception ex)
        {
            LogFailure(ex, "capture");
        }
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
