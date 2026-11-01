namespace Lertaro.Plugins.ClipboardHistory.Capture;

internal sealed record ClipboardCaptureOptions(bool Enabled, int MaxTextLength, string ExcludedProcesses);
