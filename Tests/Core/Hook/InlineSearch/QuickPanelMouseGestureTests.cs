using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class QuickPanelMouseGestureTests
{
    [TestMethod]
    public void ShortClickAndDraggingKeepNormalRightButtonRelease()
    {
        var detector = new QuickPanelMouseGestureDetector();
        detector.BeginHold(100, 100, 1000, 600, 6);
        Assert.IsFalse(detector.TryFireHold(1599)); Assert.IsFalse(detector.Release());
        detector.BeginHold(100, 100, 2000, 600, 6); detector.Move(107, 100);
        Assert.IsFalse(detector.TryFireHold(2700)); Assert.IsFalse(detector.Release());
    }
    [TestMethod]
    public void LongHoldFiresOnceAndSuppressesOnlyItsOwnReleaseIncludingTickRollover()
    {
        var detector = new QuickPanelMouseGestureDetector();
        detector.BeginHold(-1000, 100, uint.MaxValue - 299, 600, 6);
        Assert.IsTrue(detector.TryFireHold(300)); Assert.IsFalse(detector.TryFireHold(900));
        detector.CancelPendingHold(); Assert.IsTrue(detector.Release()); Assert.IsFalse(detector.Release());
    }
    [TestMethod]
    public void CancelBeforeThresholdDoesNotConsumeClick()
    {
        var detector = new QuickPanelMouseGestureDetector(); detector.BeginHold(0, 0, 1, 600, 6);
        detector.CancelPendingHold(); Assert.IsFalse(detector.TryFireHold(1000)); Assert.IsFalse(detector.Release());
    }
    [TestMethod]
    [DataRow(1d, 1d, 0)]
    [DataRow(-1d, 1d, -1920)]
    [DataRow(1d, 2d, -3840)]
    public void TwoCirclesWorkInEitherDirectionAndAcrossScaledNegativeCoordinates(double direction, double scale, int origin)
    {
        var detector = new QuickPanelMouseGestureDetector(); var count = 0;
        for (var i = 0; i <= 96; i++)
        {
            var angle = direction * i * Math.Tau / 48;
            if (detector.AddCirclePoint(origin + (int)(70 * scale * Math.Cos(angle)), (int)(70 * scale * Math.Sin(angle)), (uint)(1000 + i * 18), scale)) count++;
        }
        Assert.AreEqual(1, count);
    }
    [TestMethod]
    public void OneCircleStraightMotionSmallJitterAndSlowMotionDoNotTrigger()
    {
        foreach (var mode in new[] { 0, 1, 2, 3 })
        {
            var detector = new QuickPanelMouseGestureDetector();
            for (var i = 0; i <= 96; i++)
            {
                var angle = i * Math.Tau / (mode == 0 ? 96 : 48);
                var x = mode == 1 ? i * 5 : (int)((mode == 2 ? 10 : 70) * Math.Cos(angle));
                var y = mode == 1 ? 0 : (int)((mode == 2 ? 10 : 70) * Math.Sin(angle));
                Assert.IsFalse(detector.AddCirclePoint(x, y, (uint)(1000 + i * (mode == 3 ? 100 : 18))), $"mode={mode},point={i}");
            }
        }
    }
    [TestMethod]
    public void ResetPreventsJoiningCirclesFromDifferentWindows()
    {
        var detector = new QuickPanelMouseGestureDetector();
        for (var loop = 0; loop < 3; loop++)
        {
            detector.ResetCircle();
            for (var i = 0; i <= 48; i++)
                Assert.IsFalse(detector.AddCirclePoint((int)(70 * Math.Cos(i * Math.Tau / 48)), (int)(70 * Math.Sin(i * Math.Tau / 48)), (uint)(1000 + loop * 1000 + i * 18)));
        }
    }

    [TestMethod]
    public void MouseAllowlistDistinguishesDesktopFromExplorerAndBlocklistsWin()
    {
        var settings = new UserSettings();
        Assert.IsTrue(QuickPanelMouseWakePolicy.Allows(settings, false, true, "explorer", "explorer", false));
        Assert.IsFalse(QuickPanelMouseWakePolicy.Allows(settings, false, false, "explorer", "explorer", false));
        settings.Hotkeys.QuickPanelMouseAllowedProcesses.Add("EXPLORER.EXE");
        Assert.IsTrue(QuickPanelMouseWakePolicy.Allows(settings, false, false, "explorer", "explorer", false));
        settings.Hotkeys.QuickPanelMouseAllowDesktop = false;
        Assert.IsFalse(QuickPanelMouseWakePolicy.Allows(settings, false, true, "explorer", "explorer", false));
        settings.Hotkeys.QuickPanelMouseAllowedProcesses.Add("editor.exe");
        Assert.IsTrue(QuickPanelMouseWakePolicy.Allows(settings, false, false, "EDITOR", "editor", false));
        settings.QuickPanel.BlacklistedProcesses.Add("editor");
        Assert.IsFalse(QuickPanelMouseWakePolicy.Allows(settings, false, false, "EDITOR", "editor", false));
        settings.QuickPanel.BlacklistedProcesses.Clear(); settings.BlacklistedProcesses.Add("editor.exe");
        Assert.IsFalse(QuickPanelMouseWakePolicy.Allows(settings, false, false, "EDITOR", "editor", false));
        Assert.IsFalse(QuickPanelMouseWakePolicy.Allows(settings, false, false, "explorer", "editor", false));
    }
    [TestMethod]
    public void DisabledPanelSuspensionAndFullscreenAreHonored()
    {
        var settings = new UserSettings();
        Assert.IsFalse(QuickPanelMouseWakePolicy.Allows(settings, true, true, "explorer", "explorer", false));
        Assert.IsFalse(QuickPanelMouseWakePolicy.Allows(settings, false, true, "explorer", "explorer", true));
        settings.Hotkeys.AllowHotkeysInFullscreen = true;
        Assert.IsTrue(QuickPanelMouseWakePolicy.Allows(settings, false, true, "explorer", "explorer", true));
        settings.QuickPanel.Enabled = false;
        Assert.IsFalse(QuickPanelMouseWakePolicy.Allows(settings, false, true, "explorer", "explorer", false));
    }
    [TestMethod]
    public void AlternateKeyPreservesOriginalAndRecognizesInlineChordOwnership()
    {
        var settings = new UserSettings(); settings.Hotkeys.QuickPanelAlternateHotkey = "Alt+Space";
        var detector = new GlobalHotkeyDetector(settings, new Core.Hook.ExplorerTracker());
        detector.OnKeyDown(0xA2); Assert.IsTrue(detector.CheckQuickPanelHotkey(0x71, out var consume)); Assert.IsTrue(consume);
        detector.OnKeyUp(0xA2); detector.OnKeyDown(0xA4);
        Assert.IsTrue(detector.IsQuickPanelComboDown(0x20)); Assert.IsTrue(detector.CheckQuickPanelHotkey(0x20, out consume));
        Assert.IsTrue(consume); Assert.IsFalse(detector.CheckQuickPanelHotkey(0x71, out _));
    }
}
