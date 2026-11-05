using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

[TestClass]
public sealed class ClipboardSensitivityTests
{
    private const uint ExcludeAll = 0xC001;
    private const uint IncludeInHistory = 0xC002;

    [TestMethod]
    public void ExcludeAllMarker_SkipsCapture()
    {
        var native = new FakeNative();
        native.AvailableFormats.Add((int)ExcludeAll);

        Assert.IsTrue(ClipboardSensitivity.ShouldSkip(native, 1, 7, 1));
    }

    [TestMethod]
    public void IncludeInHistory_Zero_SkipsCapture()
    {
        var native = new FakeNative();
        native.AvailableFormats.Add((int)IncludeInHistory);
        native.FormatPayload[(int)IncludeInHistory] = 0;

        Assert.IsTrue(ClipboardSensitivity.ShouldSkip(native, 1, 7, 1));
    }

    [TestMethod]
    public void IncludeInHistory_NonZero_AllowsCapture()
    {
        var native = new FakeNative();
        native.AvailableFormats.Add((int)IncludeInHistory);
        native.FormatPayload[(int)IncludeInHistory] = 1;

        Assert.IsFalse(ClipboardSensitivity.ShouldSkip(native, 1, 7, 1));
    }

    [TestMethod]
    public void NoMarkers_AllowsCapture()
    {
        Assert.IsFalse(ClipboardSensitivity.ShouldSkip(new FakeNative(), 1, 7, 1));
    }

    [TestMethod]
    public void UnopenableClipboard_FailsClosed()
    {
        var native = new FakeNative { FailAllOpens = true };
        native.AvailableFormats.Add((int)ExcludeAll); // would be readable, but the open never succeeds

        Assert.IsTrue(ClipboardSensitivity.ShouldSkip(native, 1, 7, 1));
    }

    [TestMethod]
    public void ChangedClipboard_UnderTheCheck_ProceedsToTheReaders()
    {
        // A changed clipboard is not ours to judge: returning false lets the readers run, and they
        // bail on their own snapshot mismatch -- the next WM_CLIPBOARDUPDATE owns the new content.
        var native = new FakeNative { SequenceValue = 8 };
        native.AvailableFormats.Add((int)ExcludeAll);

        Assert.IsFalse(ClipboardSensitivity.ShouldSkip(native, 1, 7, 1));
    }

    [TestMethod]
    public void IncludeInHistory_WithoutPayload_AllowsCapture()
    {
        // Only an explicit zero excludes; a marker with no readable DWORD is not a verdict.
        var native = new FakeNative();
        native.AvailableFormats.Add((int)IncludeInHistory);

        Assert.IsFalse(ClipboardSensitivity.ShouldSkip(native, 1, 7, 1));
    }

    // Hand-written fake per repo convention: registers the two known marker names to stable ids,
    // serves per-format availability, and hands out a four-byte DWORD payload where configured.
    private sealed class FakeNative : IClipboardNative
    {
        public bool FailAllOpens;
        public uint SequenceValue = 7;
        public nint OwnerValue = 1;
        public HashSet<int> AvailableFormats = [];
        public Dictionary<int, uint> FormatPayload = [];
        private uint _servedPayload;

        public uint GetClipboardSequenceNumber() => SequenceValue;
        public nint GetClipboardOwner() => OwnerValue;
        public uint GetWindowThreadProcessId(nint hWnd, out int processId) { processId = 0; return 0; }
        public bool AddClipboardFormatListener(nint hWnd) => true;
        public bool RemoveClipboardFormatListener(nint hWnd) => true;
        public bool IsClipboardFormatAvailable(int format) => AvailableFormats.Contains(format);
        public bool OpenClipboard(nint hWndNewOwner) => !FailAllOpens;
        public bool CloseClipboard() => true;

        public nint GetClipboardData(int format)
        {
            _servedPayload = FormatPayload.TryGetValue(format, out var value) ? value : 0;
            return FormatPayload.ContainsKey(format) ? 1 : nint.Zero;
        }

        public nuint GlobalSize(nint hMem) => sizeof(uint);

        public nint GlobalLock(nint hMem)
        {
            var ptr = Marshal.AllocHGlobal(sizeof(uint));
            Marshal.WriteInt32(ptr, unchecked((int)_servedPayload)); // the real clipboard hands out initialized memory
            return ptr;
        }

        public bool GlobalUnlock(nint hMem) { Marshal.FreeHGlobal(hMem); return true; }
        public uint RegisterClipboardFormat(string formatName) => formatName.Contains("Exclude") ? ExcludeAll : IncludeInHistory;
        public uint DragQueryFileCount(nint hDrop) => 0;
        public int DragQueryFileName(nint hDrop, uint index, char[] buffer) => 0;
    }
}
