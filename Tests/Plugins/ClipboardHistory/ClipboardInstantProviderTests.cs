namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardInstantProviderTests
{
    [TestMethod]
    public void MatchesLiteralContentWithPinsFirstThenRecency()
    {
        ClipboardEntry[] entries = [Entry(1, "alpha", false, 100), Entry(2, "ALPHA", true, 1),
            Entry(3, "alphabet", false, 200), Entry(4, "a_l_p_h_a", true, 300)];
        CollectionAssert.AreEqual(new long[] { 2, 3, 1 },
            ClipboardInstantProvider.MatchEntries(entries, "alpha").Select(entry => entry.Id).ToArray());
    }

    [TestMethod]
    public void MatchesContentBeyondPreviewWithoutInventingPreviewHighlights()
    {
        var entry = Entry(1, new string('x', 210) + "needle", false, 1);
        Assert.HasCount(1, ClipboardInstantProvider.MatchEntries([entry], "needle").ToArray());
        Assert.IsFalse(ClipboardInstantProvider.Highlight(entry.PlainPreview, "needle").Any(value => value));
    }

    [TestMethod]
    public void LiteralHighlightsIncludeOverlappingMatchesAndKeepPreviewLength()
    {
        CollectionAssert.AreEqual(new[] { true, true, true, true, true, false },
            ClipboardInstantProvider.Highlight("aBaBa!", "aba"));
        Assert.HasCount(0, ClipboardInstantProvider.Highlight("", "word"));
        Assert.IsFalse(ClipboardInstantProvider.Highlight("text", "").Any(value => value));
    }

    internal static ClipboardEntry Entry(long id, string content, bool pinned, long time) =>
        new(id, content, content[..Math.Min(200, content.Length)], "editor", "Document", pinned, 1, time, time);
}
