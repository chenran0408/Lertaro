namespace Lertaro.Plugins.ClipboardHistory.Tests.View;
using Lertaro.Plugins.ClipboardHistory.View;

[TestClass]
public sealed class ClipboardPanelTextTests
{
    [TestMethod]
    public void Split_NullOrAbsentMask_YieldsOnePlainSegment()
    {
        var segments = ClipboardPanelText.Split("hello", null);
        Assert.HasCount(1, segments);
        Assert.IsFalse(segments[0].Highlighted);
        Assert.AreEqual("hello", segments[0].Text);
    }

    [TestMethod]
    public void Split_AlternatingMask_ProducesAlternatingSegments()
    {
        //             h  e  l  l  o
        var mask = new[] { true, true, false, false, true };
        var segments = ClipboardPanelText.Split("hello", mask);

        Assert.HasCount(3, segments);
        Assert.AreEqual(("he", true), (segments[0].Text, segments[0].Highlighted));
        Assert.AreEqual(("ll", false), (segments[1].Text, segments[1].Highlighted));
        Assert.AreEqual(("o", true), (segments[2].Text, segments[2].Highlighted));
    }

    [TestMethod]
    public void Split_SurrogatePairNeverBroken()
    {
        var emoji = "😀";
        var text = "a" + emoji + "b";
        // Mask flags only the pair; the pair must stay in one segment even though the edges
        // around it are highlighted too.
        var mask = new[] { true, true, true, false };
        var segments = ClipboardPanelText.Split(text, mask);

        Assert.HasCount(2, segments);
        Assert.AreEqual("a" + emoji, segments[0].Text);
        Assert.AreEqual("b", segments[1].Text);
        Assert.IsTrue(segments[0].Highlighted);
        Assert.IsFalse(segments[1].Highlighted);

        // A mask break between the halves is ignored: the pair travels together.
        var splitMask = new[] { false, true, false, false };
        var split = ClipboardPanelText.Split(text, splitMask);
        Assert.HasCount(3, split);
        Assert.AreEqual(emoji, split[1].Text);
    }

    [TestMethod]
    public void Split_EmptyText_NoSegments()
    {
        Assert.IsEmpty(ClipboardPanelText.Split("", [true, true]));
    }

    [TestMethod]
    public void QuotedPaths_EachPathDoubleQuotedOnePerLine()
    {
        Assert.AreEqual("\"C:\\a.txt\"\r\n\"C:\\b b.txt\"",
            ClipboardPanelText.QuotedPaths([@"C:\a.txt", @"C:\b b.txt"]));
    }

    [TestMethod]
    public void WslPaths_ConvertsDriveUncAndLeavesOthers()
    {
        Assert.AreEqual("/mnt/c/dir/file.txt", ClipboardPanelText.WslPaths([@"C:\dir\file.txt"]).Split('\n')[0]);
        Assert.AreEqual("/mnt/c/dir", ClipboardPanelText.WslPaths([@"c:\dir\"]).Split('\n')[0]);
        Assert.AreEqual("/mnt/server/share/f.txt", ClipboardPanelText.WslPaths([@"\\server\share\f.txt"]).Split('\n')[0]);
    }
}
