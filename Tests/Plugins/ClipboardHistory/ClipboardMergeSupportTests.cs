namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardMergeSupportTests
{
    private static ClipboardEntry Text(string content) =>
        new(0, content, content, "app", "", false, 1, 0, 0, ClipboardEntryKind.Text);

    private static ClipboardEntry Files(params string[] paths) =>
        new(0, string.Join("\n", paths), "", "app", "", false, 1, 0, 0, ClipboardEntryKind.FilePaths);

    private static ClipboardEntry Image() =>
        new(0, "", "100×100", "app", "", false, 1, 0, 0, ClipboardEntryKind.Image, "a.png", 100, 100);

    [TestMethod]
    public void SingleTextSelection_IsNotMergedThroughThisPath()
    {
        // The panel only routes multi-select here; MergeForPaste still accepts one entry sanely.
        Assert.AreEqual("one", ClipboardMergeSupport.MergeForPaste([Text("one")])!.Content);
    }

    [TestMethod]
    public void MultipleTexts_JoinWithLineBreaksInSelectionOrder()
    {
        Assert.AreEqual("first\rsecond".Replace("\r", "\n"),
            ClipboardMergeSupport.MergeForPaste([Text("first"), Text("second")])!.Content);
    }

    [TestMethod]
    public void FileLists_UnionDedupedCaseInsensitively()
    {
        var merged = ClipboardMergeSupport.MergeForPaste([Files(@"C:\a.txt", @"C:\b.txt"), Files(@"c:\A.TXT", @"C:\c.txt")])!;

        Assert.AreEqual(ClipboardEntryKind.FilePaths, merged.Kind);
        Assert.HasCount(3, merged.PathList);
        Assert.AreEqual(@"C:\a.txt", merged.PathList[0]);
    }

    [TestMethod]
    public void MixedSelection_TextsFirstThenPaths()
    {
        var merged = ClipboardMergeSupport.MergeForPaste([Text("note"), Files(@"C:\a.txt"), Text("second")])!;

        Assert.AreEqual(ClipboardEntryKind.Text, merged.Kind);
        Assert.AreEqual("note" + "\n" + "second" + "\n" + @"C:\a.txt", merged.Content);
    }

    [TestMethod]
    public void ImagesOnly_NothingToPaste()
    {
        Assert.IsNull(ClipboardMergeSupport.MergeForPaste([Image(), Image()]));
    }

    [TestMethod]
    public void EmptyTextRows_Skipped()
    {
        Assert.IsNull(ClipboardMergeSupport.MergeForPaste([Text("")]));
    }
}
