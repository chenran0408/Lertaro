namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardInstantProviderTests
{
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
