using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Reads CF_UNICODETEXT from the clipboard, retrying OpenClipboard while another
// window holds it open. The read is bounded to maxTextLength characters so an
// oversized clipboard entry never allocates more than the configured limit.
internal sealed class ClipboardTextReader
{
    internal const int MaxRetries = 2; // initial attempt plus 2 retries
    internal const int RetryIntervalMs = 50;

    private readonly IClipboardNative _native;
    private readonly nint _openWindow;

    public ClipboardTextReader(IClipboardNative native, nint openWindow)
    {
        _native = native;
        _openWindow = openWindow;
    }

    // expectedSequence/expectedOwner is the snapshot taken by the monitor before
    // opening. After each successful open the live clipboard is validated against
    // it: under contention the clipboard may have changed while the open was
    // failing, and reading then could attribute another app's text (possibly an
    // excluded password manager) to the snapshotted owner. On mismatch we skip;
    // the next WM_CLIPBOARDUPDATE covers the new content.
    public string? Read(int maxTextLength, uint expectedSequence, nint expectedOwner)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0) Thread.Sleep(RetryIntervalMs);
            if (!_native.OpenClipboard(_openWindow)) continue;
            try
            {
                if (_native.GetClipboardSequenceNumber() != expectedSequence ||
                    _native.GetClipboardOwner() != expectedOwner) return null;
                if (!_native.IsClipboardFormatAvailable(IClipboardNative.CfUnicodeText)) return null;
                return ReadText(maxTextLength);
            }
            finally
            {
                _native.CloseClipboard();
            }
        }
        return null;
    }

    private string? ReadText(int maxTextLength)
    {
        if (maxTextLength <= 0) return null;
        var handle = _native.GetClipboardData(IClipboardNative.CfUnicodeText);
        if (handle == nint.Zero) return null;

        var sizeBytes = _native.GlobalSize(handle);
        if (sizeBytes == nuint.Zero) return null; // allocation size unknown: skip instead of risking an over-read

        var ptr = _native.GlobalLock(handle);
        if (ptr == nint.Zero) return null;
        try
        {
            var charCap = (int)Math.Min((nuint)maxTextLength, sizeBytes / 2);
            var buffer = new char[charCap];
            Marshal.Copy(ptr, buffer, 0, charCap);
            var terminator = Array.IndexOf(buffer, '\0');
            if (terminator == 0) return null; // empty text
            if (terminator >= 0) return new string(buffer, 0, terminator);
            // Truncated without reaching the terminator: never end on a dangling
            // high surrogate, the store would receive a broken trailing pair.
            var text = new string(buffer);
            return text.Length > 0 && char.IsHighSurrogate(text[^1]) ? text[..^1] : text;
        }
        finally
        {
            _native.GlobalUnlock(handle);
        }
    }
}
