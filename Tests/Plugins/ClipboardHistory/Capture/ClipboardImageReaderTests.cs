using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

[TestClass]
public sealed class ClipboardImageReaderTests
{
    private static BitmapSource CreateBitmap()
    {
        var pixels = new byte[2 * 2 * 4];
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 2 * 4);
        if (source.CanFreeze) source.Freeze();
        return source;
    }

    [TestMethod]
    public void Succeeds_After_Transient_Contention()
    {
        var native = new FakeImageNative();
        var calls = 0;
        Func<BitmapSource?> getImage = () =>
        {
            calls++;
            return calls <= 2
                ? throw new COMException("busy", unchecked((int)0x800401D0))
                : CreateBitmap();
        };
        var reader = new ClipboardImageReader(native, getImage);

        var payload = reader.Read(7, 1);

        Assert.IsNotNull(payload);
        Assert.AreEqual(3, calls);
        Assert.IsTrue(payload.Png.Length > 0);
    }

    [TestMethod]
    public void Skips_When_Snapshot_Changes_Under_Retry()
    {
        var native = new FakeImageNative();
        var calls = 0;
        Func<BitmapSource?> getImage = () =>
        {
            calls++;
            native.SequenceValue++; // the owner copied something else while we were held off
            throw new COMException("busy", unchecked((int)0x800401D0));
        };
        var reader = new ClipboardImageReader(native, getImage);

        Assert.IsNull(reader.Read(7, 1));
        Assert.AreEqual(1, calls); // no second attempt against foreign content
    }

    [TestMethod]
    public void Gives_Up_After_Retry_Budget()
    {
        var native = new FakeImageNative();
        var calls = 0;
        Func<BitmapSource?> getImage = () =>
        {
            calls++;
            throw new InvalidOperationException("clipboard busy");
        };
        var reader = new ClipboardImageReader(native, getImage);

        Assert.IsNull(reader.Read(7, 1));
        Assert.AreEqual(ClipboardImageReader.MaxRetries + 1, calls);
    }

    [TestMethod]
    public void Does_Not_Call_GetImage_When_Snapshot_Already_Changed()
    {
        var native = new FakeImageNative { SequenceValue = 8 };
        var calls = 0;
        Func<BitmapSource?> getImage = () => { calls++; return CreateBitmap(); };
        var reader = new ClipboardImageReader(native, getImage);

        Assert.IsNull(reader.Read(7, 1));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void Null_Source_Retries_Within_Budget_Then_Gives_Up()
    {
        var native = new FakeImageNative();
        var calls = 0;
        Func<BitmapSource?> getImage = () => { calls++; return null; };
        var reader = new ClipboardImageReader(native, getImage);

        Assert.IsNull(reader.Read(7, 1));
        Assert.AreEqual(ClipboardImageReader.MaxRetries + 1, calls);
    }

    // Hand-written fake per repo convention (no mocking frameworks): the image reader only
    // consults the sequence/owner snapshot, so the other members stay inert.
    private sealed class FakeImageNative : IClipboardNative
    {
        public uint SequenceValue = 7;
        public nint OwnerValue = 1;

        public uint GetClipboardSequenceNumber() => SequenceValue;
        public nint GetClipboardOwner() => OwnerValue;
        public uint GetWindowThreadProcessId(nint hWnd, out int processId) { processId = 0; return 0; }
        public bool AddClipboardFormatListener(nint hWnd) => true;
        public bool RemoveClipboardFormatListener(nint hWnd) => true;
        public bool IsClipboardFormatAvailable(int format) => true;
        public bool OpenClipboard(nint hWndNewOwner) => true;
        public bool CloseClipboard() => true;
        public nint GetClipboardData(int format) => nint.Zero;
        public nuint GlobalSize(nint hMem) => 0;
        public nint GlobalLock(nint hMem) => nint.Zero;
        public bool GlobalUnlock(nint hMem) => true;
        public uint RegisterClipboardFormat(string formatName) => 0;
        public uint DragQueryFileCount(nint hDrop) => 0;
        public int DragQueryFileName(nint hDrop, uint index, char[] buffer) => 0;
    }
}
