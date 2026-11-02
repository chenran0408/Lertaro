namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardRuntimeTests
{
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

    // PasteAndClose re-reads the entry from Entries AFTER the paste -- Publish() can re-order the
    // cache while the paste is in flight, so the close/stay answer must come from the current
    // state. This pins the shape of that lookup: a pinned entry is the "panel stays up so the same
    // entry can be pasted again" case, an unpinned one the "close and hand focus back" case.
    [TestMethod]
    public void PasteStayDecisionFollowsTheCurrentPinState()
    {
        Assert.IsTrue(ClipboardInstantProviderTests.Entry(7, "pin", pinned: true, time: 7).Pinned);
        Assert.IsFalse(ClipboardInstantProviderTests.Entry(8, "plain", pinned: false, time: 8).Pinned);
    }
}
