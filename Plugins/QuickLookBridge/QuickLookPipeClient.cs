using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.QuickLookBridge;

// Talks directly to QuickLook's (github.com/QL-Win/QuickLook) named pipe -- the exact same one its own
// CLI second-instance forwarding uses -- instead of spawning a process per preview. Pipe name and message
// IDs below are copied verbatim from QuickLook/PipeServerManager.cs; they're QuickLook's private
// implementation detail, not a published API, so a future QuickLook release could change them without
// notice and silently break this.
internal static class QuickLookPipeClient
{
    private const int ConnectTimeoutMs = 1000;

    // QuickLook.App.PipeMessages.Toggle, sent WITH a non-empty options string -- QuickLook's own
    // TogglePreview(path, options) only takes its "hide if already showing this same path" branch when
    // options is empty; a non-empty options string always routes to InvokePreviewWithOption(path,
    // options) instead (see ViewWindowManager.cs), i.e. plain show/update, same as the Invoke message,
    // plus whatever the options ask for. Used (with "-top") to keep the docked window topmost so it
    // doesn't get lost behind Lertaro's own window.
    private const string ToggleMessage = "QuickLook.App.PipeMessages.Toggle";

    // Must have a "-"/"--"/"/" prefix -- QuickLook's own CommandLineParser (Helpers/CommandLineParser.cs)
    // only records a bare word into its Values dictionary if it immediately follows an already-recorded
    // "-key" token; a lone word with nothing preceding it (exactly what a bare "top" is here, since it's
    // the only token) is silently dropped and cli.Has("top") never sees it. Confirmed against QuickLook's
    // actual source, not just its own doc comments -- the first attempt at this used a bare "top" and
    // silently had no effect.
    private const string TopOption = "-top";

    // QuickLook.App.PipeMessages.Close -- NOT just a hide: ClosePreview() calls _viewerWindow.Close(),
    // and its Closed event handler immediately does _viewerWindow = new ViewerWindow() (confirmed against
    // QuickLook's own source, ViewWindowManager.cs). So every Close we send genuinely destroys and
    // recreates QuickLook's window -- any cached hwnd from before this point is guaranteed stale
    // afterward, not just possibly stale (see TryClosePreview).
    private const string CloseMessage = "QuickLook.App.PipeMessages.Close";

    // Anything QuickLook's own PipeMessages switch doesn't recognize falls into its `default: return
    // false` branch -- a real message ID with zero visible side effect, used purely to test reachability.
    private const string PingMessage = "Lertaro.Plugins.QuickLookBridge.Ping";

    private static readonly string PipeName =
        "QuickLook.App.Pipe." + (WindowsIdentity.GetCurrent().User?.Value ?? string.Empty);

    private static readonly QuickLookAvailability Availability = new(
        () => TrySend(PingMessage, string.Empty, string.Empty), QuickLookWindowPositioner.Reset);

    // Automatic preview retains its first-use availability check. Menus always return immediately.
    public static bool IsAvailable() => Availability.Read(waitForInitial: true);
    public static bool IsAvailableForMenu() => Availability.Read(waitForInitial: false);

    // Also called synchronously from the UI thread on every single navigation (CreatePreview/
    // TrySetTarget/EndPreviewSession). Originally queued strictly in order (one Task.ContinueWith chain),
    // to guarantee an earlier Invoke's send never completed after a later one's -- but a strict FIFO
    // queue is exactly wrong when requests arrive faster than they can be sent: rapidly paging through a
    // long result list (arrow-key-held or fast scroll, observed at 20-50ms per file -- far faster than one
    // real pipe round trip) queued up dozens of sends, and since each one really did get sent in order,
    // QuickLook just fell further and further behind, showing files the user had already scrolled past
    // long before catching up to the current one -- which reads as "invoke failure" even though every
    // individual send succeeded. What actually matters is only ever the MOST RECENT request: if a newer
    // one arrives before the previous one has even started sending, the old one is already irrelevant and
    // is dropped instead of queued, so the worker only ever sends whatever was truly latest once it's free.
    private enum PendingKind { None, Invoke, Close }

    private static readonly object QueueLock = new();
    private static PendingKind _pendingKind;
    private static string _pendingPath = string.Empty;
    private static bool _workerRunning;

    public static void TryInvokePreview(string path) => Enqueue(PendingKind.Invoke, path);

    public static void TryClosePreview()
    {
        // Reset BEFORE sending, not after: QuickLook destroys and recreates its window as a direct,
        // synchronous(-ish) consequence of receiving this message (see CloseMessage's own comment), so the
        // cached hwnd is guaranteed stale from the moment this is sent, not just probably stale by the
        // time the send completes. Safe to do even if this particular Close ends up superseded/dropped
        // below before ever actually being sent -- resetting our own local cache early is harmless either
        // way, at worst costing one extra re-poll later.
        QuickLookWindowPositioner.Reset();
        Enqueue(PendingKind.Close, string.Empty);
    }

    private static void Enqueue(PendingKind kind, string path)
    {
        lock (QueueLock)
        {
            _pendingKind = kind;
            _pendingPath = path;
            if (_workerRunning) return;
            _workerRunning = true;
        }
        Task.Run(RunPendingWorker);
    }

    private static void RunPendingWorker()
    {
        while (true)
        {
            PendingKind kind;
            string path;
            lock (QueueLock)
            {
                if (_pendingKind == PendingKind.None)
                {
                    _workerRunning = false;
                    return;
                }
                kind = _pendingKind;
                path = _pendingPath;
                _pendingKind = PendingKind.None;
            }

            if (kind == PendingKind.Invoke)
                TrySend(ToggleMessage, path, TopOption);
            else
                TrySend(CloseMessage, string.Empty, string.Empty);
        }
    }

    private static bool TrySend(string pipeMessage, string path, string options)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);

            // QuickLook's server does an unconditional reader.ReadLine() with no null guard, so a client
            // that connects without ever writing a line crashes its read loop (NullReferenceException on
            // the null result) and takes down the pipe for the rest of that QuickLook session -- always
            // write a real line, even for the no-op ping.
            using var writer = new StreamWriter(client);
            writer.WriteLine($"{pipeMessage}|{path}|{options}");
            writer.Flush();
            Logger.Log($"[QuickLookBridge] pipe send ok: {pipeMessage} '{path}' options='{options}' (pipe='{PipeName}')", LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickLookBridge] pipe send FAILED: {pipeMessage} '{path}' options='{options}' (pipe='{PipeName}') -> {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }
}
