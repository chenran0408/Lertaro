namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardRuntimeTests
{
    [TestMethod]
    public void RetentionTracksImageAndTextDeadlinesWithoutExpiringPinnedEntries()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var time = now.AddDays(-2).ToUnixTimeMilliseconds();
        var text = ClipboardInstantProviderTests.Entry(1, "text", pinned: false, time);
        var image = ClipboardInstantProviderTests.Entry(2, "image", pinned: false, time) with { Kind = ClipboardEntryKind.Image };
        Assert.IsTrue(ClipboardRuntime.HasExpiredEntries([text, image], now, retentionDays: 30, imageRetentionDays: 1));
        Assert.IsFalse(ClipboardRuntime.HasExpiredEntries([text, image with { Pinned = true }], now, retentionDays: 30, imageRetentionDays: 1));
        Assert.IsFalse(ClipboardRuntime.HasExpiredEntries([text, image], now, retentionDays: 0, imageRetentionDays: 0));
    }

    [TestMethod]
    public void CachedOrderingMatchesPinExemptionAndUnpinnedCap()
    {
        var entries = new[]
        {
            ClipboardInstantProviderTests.Entry(1, "old pin", true, 1),
            ClipboardInstantProviderTests.Entry(2, "old", false, 2),
            ClipboardInstantProviderTests.Entry(3, "new", false, 3),
            ClipboardInstantProviderTests.Entry(4, "new pin", true, 4)
        };
        CollectionAssert.AreEqual(new long[] { 4, 1, 3 },
            ClipboardRuntime.ClipEntries(entries, 1).Select(entry => entry.Id).ToArray());
    }

}
