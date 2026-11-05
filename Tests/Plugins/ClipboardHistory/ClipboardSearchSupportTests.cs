namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardSearchSupportTests
{
    private static readonly Func<string, string, bool> Contains =
        (text, term) => text.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static ClipboardEntry Entry(string preview, string content = "", string process = "Chrome",
        ClipboardEntryKind kind = ClipboardEntryKind.Text, string note = "") =>
        new(1, content, preview, process, "window", false, 1, 0, 0, kind, Note: note);

    [TestMethod]
    public void EmptyTerm_MatchesEverything()
    {
        Assert.IsTrue(ClipboardSearchSupport.Matches(Entry(""), "", Contains));
    }

    [TestMethod]
    public void Term_MatchesPreview_Content_OrSourceProcess()
    {
        Assert.IsTrue(ClipboardSearchSupport.Matches(Entry("hello world"), "world", Contains));
        Assert.IsTrue(ClipboardSearchSupport.Matches(Entry("2 · report.docx", @"C:\docs\report.docx"), "report", Contains));
        Assert.IsTrue(ClipboardSearchSupport.Matches(Entry("1920×1080", "", process: "SnippingTool"), "snip", Contains));
        Assert.IsFalse(ClipboardSearchSupport.Matches(Entry("hello"), "xyz", Contains));
    }

    [TestMethod]
    public void ImageEntry_MatchesByDimensionsOrSource()
    {
        var image = Entry("1920×1080", "", process: "SnippingTool", kind: ClipboardEntryKind.Image);
        Assert.IsTrue(ClipboardSearchSupport.Matches(image, "1920", Contains));
        Assert.IsTrue(ClipboardSearchSupport.Matches(image, "snipping", Contains));
        Assert.IsFalse(ClipboardSearchSupport.Matches(image, "photo", Contains));
    }

    [TestMethod]
    public void Note_Matches_EvenWhenPreviewAndContentDoNot()
    {
        var noted = Entry("jpg blob", "", note: "季度报表的下载链接");
        Assert.IsTrue(ClipboardSearchSupport.Matches(noted, "季度", Contains));
        Assert.IsTrue(ClipboardSearchSupport.Matches(noted, "报表", Contains));
        // The note must not shadow a miss: a term inside nothing matches nothing.
        Assert.IsFalse(ClipboardSearchSupport.Matches(Entry("hello", note: "meeting notes"), "xyz", Contains));
    }
}
