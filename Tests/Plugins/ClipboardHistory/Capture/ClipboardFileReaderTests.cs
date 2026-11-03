namespace Lertaro.Plugins.ClipboardHistory.Tests.Capture;
using Lertaro.Plugins.ClipboardHistory.Capture;

[TestClass]
public sealed class ClipboardFileReaderTests
{
    private string _databasePath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"clipboard-drop-{Guid.NewGuid():N}.bin");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    [TestMethod]
    public void Read_ValidDrop_ReturnsAllPathsInOrder()
    {
        var native = new FakeDropNative
        {
            Paths = [@"C:\a.txt", @"C:\b.txt", @"C:\报告.docx"],
        };
        var reader = new ClipboardFileReader(native, openWindow: 123);

        var paths = reader.Read(expectedSequence: 7, expectedOwner: 42);

        Assert.HasCount(3, paths);
        Assert.AreEqual(@"C:\a.txt", paths[0]);
        Assert.AreEqual(@"C:\报告.docx", paths[2]);
    }

    [TestMethod]
    public void Read_SequenceChangedUnderRead_ReturnsNull()
    {
        var native = new FakeDropNative { Paths = [@"C:\a.txt"], LiveSequence = 8 };
        var reader = new ClipboardFileReader(native, openWindow: 123);

        Assert.IsNull(reader.Read(expectedSequence: 7, expectedOwner: 42));
    }

    [TestMethod]
    public void Read_OwnerChangedUnderRead_ReturnsNull()
    {
        var native = new FakeDropNative { Paths = [@"C:\a.txt"], LiveOwner = 99 };
        var reader = new ClipboardFileReader(native, openWindow: 123);

        Assert.IsNull(reader.Read(expectedSequence: 7, expectedOwner: 42));
    }

    [TestMethod]
    public void Read_OversizedPath_SkippedButRestKept()
    {
        var native = new FakeDropNative
        {
            Paths = [@"C:\a.txt", new string('长', ClipboardFileReader.MaxPathChars + 1), @"C:\c.txt"],
        };
        var reader = new ClipboardFileReader(native, openWindow: 123);

        var paths = reader.Read(expectedSequence: 7, expectedOwner: 42);

        Assert.HasCount(2, paths);
        Assert.AreEqual(@"C:\a.txt", paths[0]);
        Assert.AreEqual(@"C:\c.txt", paths[1]);
    }

    [TestMethod]
    public void Read_MorePathsThanTheCap_TruncatedToTheCap()
    {
        var native = new FakeDropNative
        {
            Paths = Enumerable.Range(0, ClipboardFileReader.MaxPaths + 10).Select(i => $@"C:\f{i}.txt").ToList(),
        };
        var reader = new ClipboardFileReader(native, openWindow: 123);

        var paths = reader.Read(expectedSequence: 7, expectedOwner: 42);

        Assert.HasCount(ClipboardFileReader.MaxPaths, paths);
    }

    /// <summary>Fake over the seam with an in-memory HDROP: the sequence/owner validation discipline
    /// is what these tests exercise, not Win32 itself (covered by the binding tests).</summary>
    private sealed class FakeDropNative : IClipboardNative
    {
        public IReadOnlyList<string> Paths { get; init; } = [];
        public uint LiveSequence { get; init; } = 7;
        public nint LiveOwner { get; init; } = 42;

        public uint GetClipboardSequenceNumber() => LiveSequence;
        public nint GetClipboardOwner() => LiveOwner;
        public uint GetWindowThreadProcessId(nint hWnd, out int processId) { processId = 1; return 1; }
        public bool AddClipboardFormatListener(nint hWnd) => true;
        public bool RemoveClipboardFormatListener(nint hWnd) => true;
        public bool IsClipboardFormatAvailable(int format) => format == IClipboardNative.CfHDrop;
        public bool OpenClipboard(nint hWndNewOwner) => true;
        public bool CloseClipboard() => true;
        public nint GetClipboardData(int format) => format == IClipboardNative.CfHDrop ? 777 : 0;
        public nuint GlobalSize(nint hMem) => 0;
        public nint GlobalLock(nint hMem) => 0;
        public bool GlobalUnlock(nint hMem) => true;
        public uint DragQueryFileCount(nint hDrop) => (uint)Paths.Count;
        public int DragQueryFileName(nint hDrop, uint index, char[] buffer)
        {
            var path = Paths[(int)index];
            if (path.Length > buffer.Length) return 0;
            path.CopyTo(0, buffer, 0, path.Length);
            return path.Length;
        }
    }
}
