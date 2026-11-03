
namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Reads a CF_HDROP path list from the clipboard with the same open-retry and snapshot-validation
// discipline as ClipboardTextReader: the read must not attribute another app's drop to the
// snapshotted owner, and the list is bounded so a pasted 10,000-file folder cannot stall capture.
internal sealed class ClipboardFileReader
{
    internal const int MaxPaths = 64;
    internal const int MaxPathChars = 1024;
    internal const int MaxRetries = 2; // initial attempt plus 2 retries
    internal const int RetryIntervalMs = 50;

    private readonly IClipboardNative _native;
    private readonly nint _openWindow;

    public ClipboardFileReader(IClipboardNative native, nint openWindow)
    {
        _native = native;
        _openWindow = openWindow;
    }

    /// <summary>The paths in captured order, or null when the clipboard changed under the read or
    /// stayed contended. Empty means a valid but empty drop (nothing to record).</summary>
    public IReadOnlyList<string>? Read(uint expectedSequence, nint expectedOwner)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0) Thread.Sleep(RetryIntervalMs);
            if (!_native.OpenClipboard(_openWindow)) continue;
            try
            {
                if (_native.GetClipboardSequenceNumber() != expectedSequence ||
                    _native.GetClipboardOwner() != expectedOwner) return null;
                if (!_native.IsClipboardFormatAvailable(IClipboardNative.CfHDrop)) return null;
                return ReadPaths();
            }
            finally
            {
                _native.CloseClipboard();
            }
        }
        return null;
    }

    private List<string> ReadPaths()
    {
        var drop = _native.GetClipboardData(IClipboardNative.CfHDrop);
        if (drop == nint.Zero) return [];

        var count = Math.Min(_native.DragQueryFileCount(drop), MaxPaths);
        var buffer = new char[MaxPathChars];
        var paths = new List<string>((int)count);
        for (var index = 0u; index < count; index++)
        {
            var written = _native.DragQueryFileName(drop, index, buffer);
            if (written <= 0 || written > MaxPathChars) continue; // over-long path: skip, keep the rest
            var path = new string(buffer, 0, written);
            if (path.Length > 0) paths.Add(path);
        }
        return paths;
    }
}
