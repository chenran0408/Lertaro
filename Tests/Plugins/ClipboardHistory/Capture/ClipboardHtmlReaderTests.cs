using System.Runtime.InteropServices;
using Lertaro.Plugins.ClipboardHistory.Capture;
using System.Text;

namespace Lertaro.Plugins.ClipboardHistory.Tests.Capture;

[TestClass]
public sealed class ClipboardHtmlReaderTests
{
    private const uint HtmlFormat = 49742u;

    [TestMethod]
    public void Read_ValidHtml_ReturnsRawStream()
    {
        var html = "Version:0.9\r\nStartHTML:0\r\nEndHTML:100\r\n<html><body><b>hi</b></body></html>";
        var native = new FakeHtmlNative { Html = html, LiveSequence = 7 };
        var reader = new ClipboardHtmlReader(native, openWindow: 123);

        var read = reader.Read(expectedSequence: 7, expectedOwner: 42);

        Assert.IsNotNull(read);
        Assert.AreEqual(html, read);
    }

    [TestMethod]
    public void Read_SequenceChanged_ReturnsNull()
    {
        var native = new FakeHtmlNative { Html = "<b>x</b>", LiveSequence = 8 };
        var reader = new ClipboardHtmlReader(native, openWindow: 123);

        Assert.IsNull(reader.Read(expectedSequence: 7, expectedOwner: 42));
    }

    [TestMethod]
    public void Read_NoHtmlFormat_ReturnsNull()
    {
        var native = new FakeHtmlNative { Html = null, LiveSequence = 7 };
        var reader = new ClipboardHtmlReader(native, openWindow: 123);

        Assert.IsNull(reader.Read(expectedSequence: 7, expectedOwner: 42));
    }

    [TestMethod]
    public void Read_OversizedHtml_ReturnsNull()
    {
        var native = new FakeHtmlNative
        {
            HtmlBytes = new byte[ClipboardHtmlReader.MaxHtmlBytes + 1],
            LiveSequence = 7,
        };
        var reader = new ClipboardHtmlReader(native, openWindow: 123);

        Assert.IsNull(reader.Read(expectedSequence: 7, expectedOwner: 42));
    }

    /// <summary>Fake over the seam: the HTML payload sits in an HGLOBAL allocated here and freed at
    /// dispose, so the reader's GlobalLock/GlobalCopy path is exercised for real.</summary>
    private sealed class FakeHtmlNative : IClipboardNative, IDisposable
    {
        public string? Html { get; init; }
        public byte[]? HtmlBytes { get; init; }
        public uint LiveSequence { get; init; } = 7;
        public nint LiveOwner { get; init; } = 42;

        private nint _handle;

        public uint GetClipboardSequenceNumber() => LiveSequence;
        public nint GetClipboardOwner() => LiveOwner;
        public uint GetWindowThreadProcessId(nint hWnd, out int processId) { processId = 1; return 1; }
        public bool AddClipboardFormatListener(nint hWnd) => true;
        public bool RemoveClipboardFormatListener(nint hWnd) => true;
        public bool IsClipboardFormatAvailable(int format) => Html is not null && format == (int)HtmlFormat;
        public bool OpenClipboard(nint hWndNewOwner) => true;
        public bool CloseClipboard() => true;

        public nint GetClipboardData(int format)
        {
            if (format != (int)HtmlFormat || _handle != nint.Zero) return _handle;
            var bytes = HtmlBytes ?? Encoding.UTF8.GetBytes(Html!);
            _handle = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, _handle, bytes.Length);
            return _handle;
        }

        public nuint GlobalSize(nint hMem) => (nuint)(HtmlBytes?.Length ?? Encoding.UTF8.GetBytes(Html ?? "").Length);
        public nint GlobalLock(nint hMem) => hMem;
        public bool GlobalUnlock(nint hMem) => true;
        public uint RegisterClipboardFormat(string formatName) =>
            formatName == IClipboardNative.HtmlFormatName ? HtmlFormat : 0;
        public uint DragQueryFileCount(nint hDrop) => 0;
        public int DragQueryFileName(nint hDrop, uint index, char[] buffer) => 0;

        public void Dispose()
        {
            if (_handle != nint.Zero) Marshal.FreeHGlobal(_handle);
        }
    }
}
