using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Reads the "HTML Format" clipboard flavor next to a text capture, with the same open-retry and
// snapshot-validation discipline as the text reader. The payload is the raw CF_HTML byte stream
// (version header plus fragment), stored as-is so a keep-format paste can hand it straight back.
internal sealed class ClipboardHtmlReader
{
    /// <summary>HTML above this is dropped (the plain text is still captured): a full-page copy
    /// with embedded images can reach tens of megabytes and is worth nothing on re-paste.
    /// ponytail: a hard ceiling, not a cleaner -- if real users hit it, the upgrade strips image
    /// parts or keeps only the fragment, not raising the number.</summary>
    internal const int MaxHtmlBytes = 2 * 1024 * 1024;

    internal const int MaxRetries = 2; // initial attempt plus 2 retries
    internal const int RetryIntervalMs = 50;

    private readonly IClipboardNative _native;
    private readonly nint _openWindow;
    private readonly uint _htmlFormat;

    public ClipboardHtmlReader(IClipboardNative native, nint openWindow)
    {
        _native = native;
        _openWindow = openWindow;
        _htmlFormat = native.RegisterClipboardFormat(IClipboardNative.HtmlFormatName);
    }

    /// <summary>The raw CF_HTML stream, or null when absent, oversized, or the clipboard changed
    /// under the read. Empty string means the format exists but carried nothing usable.</summary>
    public string? Read(uint expectedSequence, nint expectedOwner)
    {
        if (_htmlFormat == 0) return null;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0) Thread.Sleep(RetryIntervalMs);
            if (!_native.OpenClipboard(_openWindow)) continue;
            try
            {
                if (_native.GetClipboardSequenceNumber() != expectedSequence ||
                    _native.GetClipboardOwner() != expectedOwner) return null;
                if (!_native.IsClipboardFormatAvailable((int)_htmlFormat)) return null;
                return ReadHtml();
            }
            finally
            {
                _native.CloseClipboard();
            }
        }
        return null;
    }

    private string? ReadHtml()
    {
        var handle = _native.GetClipboardData((int)_htmlFormat);
        if (handle == nint.Zero) return null;

        var sizeBytes = _native.GlobalSize(handle);
        if (sizeBytes == nuint.Zero || sizeBytes > MaxHtmlBytes) return null;

        var ptr = _native.GlobalLock(handle);
        if (ptr == nint.Zero) return null;
        try
        {
            var buffer = new byte[(int)sizeBytes];
            Marshal.Copy(ptr, buffer, 0, buffer.Length);
            var html = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
            return html.Length == 0 ? null : html;
        }
        finally
        {
            _native.GlobalUnlock(handle);
        }
    }
}
