namespace Lertaro.Core.Tests;

[TestClass]
public sealed class HistoryRecordingPolicyTests
{
    [TestMethod]
    public void DisabledStore_DoesNotRecord()
        => Assert.IsFalse(HistoryRecordingPolicy.ShouldRecord(enabled: false));

    [TestMethod]
    public void EnabledStore_CreatesNewEntry()
        => Assert.IsTrue(HistoryRecordingPolicy.ShouldRecord(enabled: true));
}
