namespace Lertaro.Plugins.ClipboardHistory.Capture;

// One captured clipboard snapshot handed to the runtime. SourceProcessStartedAtTicks
// is process.StartTime.ToUniversalTime().Ticks so callers can re-read SourceWindow
// later and validate it still belongs to the same process instance by comparing
// ticks for equality. SourceWindow is the owner process's MainWindowHandle (zero
// when the process has none) for the "Open Source" action; the actual clipboard
// owner handle used for reading may be a hidden OLE window and is never exposed.
internal sealed record ClipboardCapture(
    string Text,
    string SourceProcess,
    string SourceWindowTitle,
    int SourceProcessId,
    long SourceProcessStartedAtTicks,
    nint SourceWindow);
