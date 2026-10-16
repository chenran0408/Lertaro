using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class InlineInputHandoffTests
{
    private static InlineInputHandoff.Key Key(ushort key = 0x4E) => new(key, 0x31, false, false);

    [TestMethod]
    public void FirstImeKeyAndFastFollowingKeys_AreReplayedOnceInOrder()
    {
        var buffer = new InlineInputHandoff();
        var first = Key();
        var second = new InlineInputHandoff.Key(0x49, 0x17, false, true);
        var generation = buffer.Begin(10, first, 100);
        Assert.IsTrue(buffer.TryAppend(10, second, 105));
        buffer.SetTarget(generation, 20);
        Assert.IsTrue(buffer.TryAppend(20, Key(0x20), 110));
        var keys = buffer.Take(generation, 20, true, 120);
        CollectionAssert.AreEqual(new[] { first, second, Key(0x20) }, keys);
        Assert.IsEmpty(buffer.Take(generation, 20, true, 121));
        Assert.IsFalse(buffer.IsPending);
    }

    [TestMethod]
    public void LateAcknowledgement_CannotConsumeANewerInputSession()
    {
        var buffer = new InlineInputHandoff();
        var old = buffer.Begin(10, Key(), 100);
        var current = buffer.Begin(30, Key(0x49), 150);
        buffer.SetTarget(old, 20);
        Assert.IsEmpty(buffer.Take(old, 20, true, 160));
        Assert.IsTrue(buffer.IsPending);
        buffer.SetTarget(current, 40);
        Assert.AreEqual((ushort)0x49, buffer.Take(current, 40, true, 170).Single().VirtualKey);
    }

    [TestMethod]
    public void ChangingToAnotherWindow_StopsCapturingAndDropsTheStalePrefix()
    {
        var buffer = new InlineInputHandoff();
        var generation = buffer.Begin(10, Key(), 100);
        buffer.SetTarget(generation, 20);
        Assert.IsFalse(buffer.TryAppend(99, Key(), 120));
        Assert.IsEmpty(buffer.Take(generation, 20, true, 130));
    }

    [TestMethod]
    [DataRow(false, 20L, 120L)]
    [DataRow(true, 99L, 120L)]
    [DataRow(true, 20L, 2201L)]
    public void MissingFocusWrongTargetOrTimeout_NeverReplays(bool focused, long target, long now)
    {
        var buffer = new InlineInputHandoff();
        var generation = buffer.Begin(10, Key(), 100);
        buffer.SetTarget(generation, 20);
        Assert.IsEmpty(buffer.Take(generation, (nint)target, focused, now));
        Assert.IsFalse(buffer.IsPending);
    }

    [TestMethod]
    public void UnavailableApp_DoesNotCaptureTypingForeverOrGrowUnbounded()
    {
        var buffer = new InlineInputHandoff();
        buffer.Begin(10, Key(), 100);
        Assert.IsFalse(buffer.TryAppend(10, Key(), 2101));
        buffer.Begin(10, Key(), 3000);
        for (var i = 1; i < 64; i++) Assert.IsTrue(buffer.TryAppend(10, Key(), 3001));
        Assert.IsFalse(buffer.TryAppend(10, Key(), 3002));
        Assert.IsFalse(buffer.IsPending);
    }

    [TestMethod]
    [DataRow(0x41, true)] [DataRow(0xBE, true)] [DataRow(0xBF, true)] [DataRow(0xE5, true)]
    [DataRow(0x70, false)] [DataRow(0x09, false)] [DataRow(0x10, false)] [DataRow(0x1B, false)]
    public void PrintableTriggerKeys_IncludePunctuationButLeaveCommandsAlone(int key, bool expected) =>
        Assert.AreEqual(expected, InlineInputHandoff.IsPrintableKey(key));
}
