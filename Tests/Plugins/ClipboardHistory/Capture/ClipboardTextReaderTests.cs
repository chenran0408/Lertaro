using System.Diagnostics;
using System.Runtime.InteropServices;
using Lertaro.Plugins.ClipboardHistory.Capture;

namespace Lertaro.Plugins.ClipboardHistory.Tests.Capture;

[TestClass]
public sealed class ClipboardTextReaderTests
{
    [TestMethod]
    public void Read_OpensOnFirstAttempt_ReturnsText()
    {
        var native = new FakeClipboardNative { Data = "hello" };
        var reader = new ClipboardTextReader(native, openWindow: 100);

        Assert.AreEqual("hello", reader.Read(1000, expectedSequence: 0, expectedOwner: 1));
        Assert.AreEqual(1, native.OpenCalls);
        Assert.AreEqual(1, native.CloseCalls);
        Assert.AreEqual(1, native.LockCalls);
        Assert.AreEqual(1, native.UnlockCalls);
    }

    [TestMethod]
    public void Read_BusyClipboardRetriesThenSucceeds()
    {
        var native = new FakeClipboardNative { Data = "hello" };
        native.OpenResults.Enqueue(false);
        native.OpenResults.Enqueue(false);
        native.OpenResults.Enqueue(true);
        var reader = new ClipboardTextReader(native, 0);
        var clock = Stopwatch.StartNew();

        Assert.AreEqual("hello", reader.Read(1000, 0, 1));
        clock.Stop();
        Assert.AreEqual(3, native.OpenCalls);

        // Two retry sleeps at 50 ms each must have actually elapsed.
        Assert.IsGreaterThanOrEqualTo(95, clock.ElapsedMilliseconds);
    }

    [TestMethod]
    public void Read_GivesUpAfterInitialPlusTwoRetries()
    {
        var native = new FakeClipboardNative { FailAllOpens = true };
        var reader = new ClipboardTextReader(native, 0);

        Assert.IsNull(reader.Read(1000, 0, 1));
        Assert.AreEqual(ClipboardTextReader.MaxRetries + 1, native.OpenCalls);
        Assert.AreEqual(0, native.CloseCalls);
    }

    [TestMethod]
    public void Read_FormatUnavailableReturnsNullWithoutRetrying()
    {
        var native = new FakeClipboardNative { FormatAvailable = false };
        var reader = new ClipboardTextReader(native, 0);

        Assert.IsNull(reader.Read(1000, 0, 1));
        Assert.AreEqual(1, native.OpenCalls);
        Assert.AreEqual(1, native.CloseCalls);
    }

    [TestMethod]
    public void Read_SequenceChangedUnderContentionSkipsWithoutReading()
    {
        // Clipboard changed while the open was contended: reading would
        // attribute another app's text to the snapshotted owner.
        var native = new FakeClipboardNative { Data = "secret", SequenceValue = 1 };
        var reader = new ClipboardTextReader(native, 0);

        Assert.IsNull(reader.Read(1000, expectedSequence: 0, expectedOwner: 1));
        Assert.AreEqual(1, native.OpenCalls);
        Assert.AreEqual(0, native.LockCalls);
        Assert.AreEqual(1, native.CloseCalls);
    }

    [TestMethod]
    public void Read_OwnerChangedUnderContentionSkipsWithoutReading()
    {
        var native = new FakeClipboardNative { Data = "secret", OwnerValue = 2 };
        var reader = new ClipboardTextReader(native, 0);

        Assert.IsNull(reader.Read(1000, expectedSequence: 0, expectedOwner: 1));
        Assert.AreEqual(0, native.LockCalls);
    }

    [TestMethod]
    public void Read_TruncatesToMaxLengthWithoutAllocatingFullText()
    {
        var native = new FakeClipboardNative { Data = new string('a', 5000) };
        var reader = new ClipboardTextReader(native, 0);

        var text = reader.Read(100, 0, 1);
        Assert.IsNotNull(text);
        Assert.AreEqual(100, text.Length);
        Assert.IsTrue(text.All(ch => ch == 'a'));
    }

    [TestMethod]
    public void Read_ReturnsFullTextWhenWithinLimit()
    {
        var native = new FakeClipboardNative { Data = new string('b', 100) };
        var reader = new ClipboardTextReader(native, 0);

        var text = reader.Read(100, 0, 1);
        Assert.IsNotNull(text);
        Assert.AreEqual(100, text.Length);
    }

    [TestMethod]
    public void Read_TruncationDropsDanglingHighSurrogate()
    {
        // 99 'a' plus an unpaired high surrogate at the truncation point.
        var native = new FakeClipboardNative { Data = new string('a', 99) + "\uD83D" };
        var reader = new ClipboardTextReader(native, 0);

        var text = reader.Read(100, 0, 1);
        Assert.IsNotNull(text);
        Assert.AreEqual(99, text.Length);
        Assert.IsTrue(text.All(ch => ch == 'a'));
    }

    [TestMethod]
    public void Read_TruncationKeepsCompleteSurrogatePair()
    {
        var native = new FakeClipboardNative { Data = new string('a', 98) + "\uD83D\uDE00" };
        var reader = new ClipboardTextReader(native, 0);

        var text = reader.Read(100, 0, 1);
        Assert.IsNotNull(text);
        Assert.AreEqual(100, text.Length);
        Assert.IsTrue(char.IsSurrogatePair(text[^2], text[^1]));
    }

    [TestMethod]
    public void Read_EmptyClipboardTextReturnsNull()
    {
        var native = new FakeClipboardNative { Data = string.Empty };
        var reader = new ClipboardTextReader(native, 0);

        Assert.IsNull(reader.Read(1000, 0, 1));
    }

    [TestMethod]
    public void Read_MissingDataHandleReturnsNull()
    {
        var native = new FakeClipboardNative { Data = null };
        var reader = new ClipboardTextReader(native, 0);

        Assert.IsNull(reader.Read(1000, 0, 1));
        Assert.AreEqual(0, native.LockCalls);
    }

    [TestMethod]
    public void Read_LockFailureReturnsNullAndSkipsUnlock()
    {
        var native = new FakeClipboardNative { Data = "hello", LockSucceeds = false };
        var reader = new ClipboardTextReader(native, 0);

        Assert.IsNull(reader.Read(1000, 0, 1));
        Assert.AreEqual(0, native.UnlockCalls);
        Assert.AreEqual(1, native.CloseCalls);
    }

    [TestMethod]
    public void Read_UnknownAllocationSizeSkipsInsteadOfOverReading()
    {
        var native = new FakeClipboardNative { Data = "hello", SizeBytesOverride = 0 };
        var reader = new ClipboardTextReader(native, 0);

        Assert.IsNull(reader.Read(1000, 0, 1));
        Assert.AreEqual(0, native.LockCalls);
    }

    // Hand-written fake per repo convention (no mocking frameworks): only the
    // members ClipboardTextReader actually calls, with scriptable Open results
    // and a configurable live sequence/owner for contention tests.
    private sealed class FakeClipboardNative : IClipboardNative
    {
        public string? Data = "hello";
        public bool FormatAvailable = true;
        public bool FailAllOpens;
        public bool LockSucceeds = true;
        public uint SequenceValue;
        public nint OwnerValue = 1;
        public Queue<bool> OpenResults = new();
        public long SizeBytesOverride = -1; // -1: derive from Data

        public int OpenCalls, CloseCalls, LockCalls, UnlockCalls;

        public uint GetClipboardSequenceNumber() => SequenceValue;
        public nint GetClipboardOwner() => OwnerValue;
        public uint GetWindowThreadProcessId(nint hWnd, out int processId) { processId = 0; return 0; }
        public bool AddClipboardFormatListener(nint hWnd) => true;
        public bool RemoveClipboardFormatListener(nint hWnd) => true;
        public bool IsClipboardFormatAvailable(int format) => FormatAvailable;

        public bool OpenClipboard(nint hWndNewOwner)
        {
            OpenCalls++;
            if (FailAllOpens) return false;
            return OpenResults.Count == 0 || OpenResults.Dequeue();
        }

        public bool CloseClipboard() { CloseCalls++; return true; }
        public nint GetClipboardData(int format) => Data == null ? nint.Zero : 1;

        public nuint GlobalSize(nint hMem) =>
            SizeBytesOverride >= 0 ? (nuint)SizeBytesOverride : (nuint)((Data!.Length + 1) * 2);

        public nint GlobalLock(nint hMem)
        {
            LockCalls++;
            if (!LockSucceeds || Data == null) return nint.Zero;
            return Marshal.StringToHGlobalUni(Data);
        }

        public bool GlobalUnlock(nint hMem) { UnlockCalls++; return true; }
        public uint RegisterClipboardFormat(string formatName) => 49742u;
        public uint DragQueryFileCount(nint hDrop) => 0;
        public int DragQueryFileName(nint hDrop, uint index, char[] buffer) => 0;
    }
}
