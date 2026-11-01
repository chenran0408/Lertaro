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
}
