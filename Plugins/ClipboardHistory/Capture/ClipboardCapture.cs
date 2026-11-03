namespace Lertaro.Plugins.ClipboardHistory.Capture;

// One captured clipboard snapshot handed to the runtime, of exactly one kind. SourceProcessStarted
// AtTicks is process.StartTime.ToUniversalTime().Ticks so callers can re-read SourceWindow later
// and validate it still belongs to the same process instance by comparing ticks for equality.
// SourceWindow is the owner process's MainWindowHandle (zero when the process has none) for the
// "Open Source" action; the actual clipboard owner handle used for reading may be a hidden OLE
// window and is never exposed. ImagePng carries the encoded PNG bytes for an Image capture and is
// empty otherwise; the runtime stores them content-addressed and lets the store dedup by hash.
internal sealed record ClipboardCapture(
    ClipboardEntryKind Kind,
    string Text,
    IReadOnlyList<string> FilePaths,
    byte[] ImagePng,
    int ImageWidth,
    int ImageHeight,
    string SourceProcess,
    string SourceWindowTitle,
    int SourceProcessId,
    long SourceProcessStartedAtTicks,
    nint SourceWindow)
{
    public static ClipboardCapture FromText(string text, ProcessIdentity identity, int processId, nint sourceWindow) =>
        new(ClipboardEntryKind.Text, text, [], [], 0, 0,
            identity.ProcessName, identity.WindowTitle, processId, identity.StartedAtUtcTicks, sourceWindow);

    public static ClipboardCapture FromFiles(IReadOnlyList<string> paths, ProcessIdentity identity, int processId, nint sourceWindow) =>
        new(ClipboardEntryKind.FilePaths, string.Empty, paths, [], 0, 0,
            identity.ProcessName, identity.WindowTitle, processId, identity.StartedAtUtcTicks, sourceWindow);

    public static ClipboardCapture FromImage(byte[] png, int width, int height, ProcessIdentity identity, int processId, nint sourceWindow) =>
        new(ClipboardEntryKind.Image, string.Empty, [], png, width, height,
            identity.ProcessName, identity.WindowTitle, processId, identity.StartedAtUtcTicks, sourceWindow);
}

// The payload of a successful image read: encoded PNG bytes plus the bitmap's pixel dimensions.
internal sealed record ClipboardImagePayload(byte[] Png, int Width, int Height)
{
    /// <summary>Images larger than this are skipped entirely: an 8-digit-pixel screenshot would
    /// otherwise dominate both disk and panel. ponytail: a hard ceiling, not a downscaler -- if real
    /// users hit it, the upgrade is resizing to a configured long edge, not raising the number.</summary>
    internal const int MaxPngBytes = 8 * 1024 * 1024;
}
