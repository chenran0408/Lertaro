using Lertaro.Plugins.ClipboardHistory.Capture;

namespace Lertaro.Plugins.ClipboardHistory.Tests.Capture;

[TestClass]
public sealed class ClipboardWriteSuppressionTests
{
    [TestMethod]
    public void ShouldSkipCapture_WithoutRecordedWrite_ReturnsFalse()
    {
        var suppression = new ClipboardWriteSuppression();
        Assert.IsFalse(suppression.ShouldSkipCapture(42));
    }

    [TestMethod]
    public void ScopedWrite_SuppressesReentrantNotifications()
    {
        var suppression = new ClipboardWriteSuppression();
        suppression.BeginWrite();

        // WPF can raise WM_CLIPBOARDUPDATE reentrantly inside SetDataObject;
        // every notification during the scoped write is our own.
        Assert.IsTrue(suppression.ShouldSkipCapture(6));
        Assert.IsTrue(suppression.ShouldSkipCapture(99));

        suppression.EndWrite(5, 6);
        Assert.IsFalse(suppression.ShouldSkipCapture(99));
    }

    [TestMethod]
    public void ShouldSkipCapture_MatchesPostWriteSequenceAcrossRepeatedNotifications()
    {
        var suppression = new ClipboardWriteSuppression();
        suppression.EndWrite(5, 6);

        // The same sequence change can notify more than once: suppression is
        // retained while the sequence still matches.
        Assert.IsTrue(suppression.ShouldSkipCapture(6));
        Assert.IsTrue(suppression.ShouldSkipCapture(6));
    }

    [TestMethod]
    public void ShouldSkipCapture_FirstDifferentSequenceEndsSuppressionWindow()
    {
        var suppression = new ClipboardWriteSuppression();
        suppression.EndWrite(5, 6);

        // An unrelated app overwrote (7) before our own event (6) was delivered:
        // its update is captured and the suppression window is closed, so a much
        // later unrelated event with the stale recorded sequence is impossible.
        Assert.IsFalse(suppression.ShouldSkipCapture(7));
        Assert.IsFalse(suppression.ShouldSkipCapture(6));
        Assert.IsFalse(suppression.ShouldSkipCapture(7));
    }

    [TestMethod]
    public void ShouldSkipCapture_SequenceBeforeWriteIsNotDropped()
    {
        var suppression = new ClipboardWriteSuppression();
        suppression.EndWrite(5, 6);

        Assert.IsFalse(suppression.ShouldSkipCapture(5));
    }

    [TestMethod]
    public void EndWrite_UnchangedSequenceArmsNothing()
    {
        var suppression = new ClipboardWriteSuppression();
        suppression.EndWrite(5, 5);

        Assert.IsFalse(suppression.ShouldSkipCapture(5));
        Assert.IsFalse(suppression.ShouldSkipCapture(6));
    }

    [TestMethod]
    public void Clear_DropsScopedAndPendingSuppression()
    {
        var suppression = new ClipboardWriteSuppression();
        suppression.BeginWrite();
        suppression.Clear();
        Assert.IsFalse(suppression.ShouldSkipCapture(6));

        suppression.BeginWrite();
        suppression.EndWrite(5, 6);
        suppression.Clear();
        Assert.IsFalse(suppression.ShouldSkipCapture(6));
    }
}
