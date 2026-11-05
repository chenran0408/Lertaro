using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Reads the clipboard's bitmap content and encodes it as PNG. ponytail: this goes through WPF's
// Clipboard.GetImage (DIB/DIBV5/CF_BITMAP decoding for free) instead of the native seam the text
// and file readers use, because hand-rolling DIB parsing is not worth it -- the trade is that the
// sequence/owner snapshot can only be validated before and after the open, not inside it. The
// pump thread this runs on is STA, which Clipboard requires.
internal sealed class ClipboardImageReader
{
    internal const int MaxRetries = 2; // initial attempt plus 2 retries
    internal const int RetryIntervalMs = 50;

    private readonly IClipboardNative _native;
    private readonly Func<BitmapSource?> _getImage;

    public ClipboardImageReader(IClipboardNative native) : this(native, Clipboard.GetImage) { }

    // Test seam: Clipboard.GetImage is a static WPF call with no native seam to fake, so the
    // contention path is exercised through this delegate instead of a live clipboard.
    internal ClipboardImageReader(IClipboardNative native, Func<BitmapSource?> getImage)
    {
        _native = native;
        _getImage = getImage;
    }

    /// <summary>The encoded PNG payload, or null when the clipboard changed under the read, the
    /// bitmap could not be decoded/encoded, or it exceeds the size ceiling.</summary>
    public ClipboardImagePayload? Read(uint expectedSequence, nint expectedOwner)
    {
        // GetImage opens the clipboard internally, so a competing reader holding it open throws
        // for a moment. Retry on the same bounded budget as the text and file readers -- but only
        // while the snapshot still matches: a changed clipboard is not ours to wait for, the next
        // WM_CLIPBOARDUPDATE owns that content.
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0) Thread.Sleep(RetryIntervalMs);
            if (_native.GetClipboardSequenceNumber() != expectedSequence ||
                _native.GetClipboardOwner() != expectedOwner) return null;

            BitmapSource? source;
            try
            {
                source = _getImage();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                continue; // clipboard contended between the availability check and the read
            }
            if (source == null) continue;

            // Re-check after the (slow) decode: a changed clipboard means the decoded pixels may not
            // belong to the snapshotted owner, who was already vetted against the exclusion list.
            if (_native.GetClipboardSequenceNumber() != expectedSequence ||
                _native.GetClipboardOwner() != expectedOwner) return null;

            if (source.CanFreeze) source.Freeze();
            return Encode(source);
        }
        return null;
    }

    private static ClipboardImagePayload? Encode(BitmapSource source)
    {
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0) return null;
        try
        {
            using var stream = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
            var png = stream.ToArray();
            if (png.Length == 0 || png.Length > ClipboardImagePayload.MaxPngBytes) return null;
            return new ClipboardImagePayload(png, source.PixelWidth, source.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
