using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class DoubleTapKeyDetectorTests
{
    [TestMethod]
    public void TwoReleasedTapsWithinWindow_TriggersOnSecondDown()
    {
        var detector = new DoubleTapKeyDetector();
        Assert.IsFalse(detector.OnKeyDown(0x70, 100));
        detector.OnKeyUp(0x70);
        Assert.IsTrue(detector.OnKeyDown(0x70, 420));
    }

    [TestMethod]
    public void TapAfterWindow_ArmsNewFirstTap()
    {
        var detector = new DoubleTapKeyDetector();
        Assert.IsFalse(detector.OnKeyDown(0x70, 100));
        detector.OnKeyUp(0x70);
        Assert.IsFalse(detector.OnKeyDown(0x70, 421));
    }

    [TestMethod]
    public void RepeatedKeyDownWhileHeld_DoesNotCountAsSecondTap()
    {
        var detector = new DoubleTapKeyDetector();
        Assert.IsFalse(detector.OnKeyDown(0x70, 100));
        Assert.IsFalse(detector.OnKeyDown(0x70, 200, repeated: true));
    }

    [TestMethod]
    public void OtherKeyBetweenTaps_ResetsSequence()
    {
        var detector = new DoubleTapKeyDetector();
        Assert.IsFalse(detector.OnKeyDown(0x70, 100));
        detector.OnKeyUp(0x70);
        detector.ResetOnOtherKey(0x71);
        Assert.IsFalse(detector.OnKeyDown(0x70, 200));
    }

    [TestMethod]
    public void TimeRollover_IsMeasuredWithUnsignedElapsedTime()
    {
        var detector = new DoubleTapKeyDetector();
        Assert.IsFalse(detector.OnKeyDown(0x70, uint.MaxValue - 100));
        detector.OnKeyUp(0x70);
        Assert.IsTrue(detector.OnKeyDown(0x70, 150));
    }
}
