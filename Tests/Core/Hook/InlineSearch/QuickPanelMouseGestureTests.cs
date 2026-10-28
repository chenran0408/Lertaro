using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class QuickPanelMouseGestureTests
{
    [TestMethod]
    public void ShortClickIsReplayedAsACompletePairAndDragHandsPressBackToSystem()
    {
        var detector = new QuickPanelMouseGestureDetector();
        detector.BeginHold(100, 100, 1000, 600, 6);
        Assert.IsTrue(detector.OwnsPress);
        Assert.IsFalse(detector.TryFireHold(1599)); Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.ReplayClick, detector.Release());
        detector.BeginHold(100, 100, 2000, 600, 6); detector.Move(107, 100);
        Assert.IsFalse(detector.TryFireHold(2700));
        Assert.IsTrue(detector.ReturnPressToSystem()); Assert.IsFalse(detector.ReturnPressToSystem());
        Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.PassThrough, detector.Release());
    }

    [TestMethod]
    public void MouseMoveWithoutCircleRulesDoesNotPollNativeKeyState()
    {
        var calls = 0;
        var settings = new UserSettings();
        using var hook = new QuickPanelMouseGestureHook(() => settings, () => false, _ => { calls++; return 0; });
        for (uint i = 0; i < 10_000; i++)
            Assert.IsFalse(hook.Process(0x0200, (int)i, 0, i * 16));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void RightHoldDownDoesNotSynchronouslyQueryWakePolicy()
    {
        var policyCalls = 0;
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelWakeBindings =
        [new QuickPanelWakeBinding { Id = "right-hold", Kind = QuickPanelWakeKind.Hold, Button = QuickPanelWakeButton.Right }];
        using var hook = new QuickPanelMouseGestureHook(() => settings, () => false,
            _ => 0, (_, _, _, _) => { policyCalls++; return true; });

        Assert.IsTrue(hook.Process(0x0204, 100, 100, 1000));
        Assert.AreEqual(0, policyCalls, "The right-button down edge must not perform process/fullscreen policy checks.");
    }

    [TestMethod]
    public void ShortRightHoldReplaysOneCompleteClickWithoutWakePolicyChecks()
    {
        var policyCalls = 0;
        var replayed = new List<(QuickPanelWakeButton Button, bool CompleteClick)>();
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelWakeBindings =
        [new QuickPanelWakeBinding { Id = "right-hold", Kind = QuickPanelWakeKind.Hold, Button = QuickPanelWakeButton.Right, HoldMilliseconds = 2000 }];
        using var hook = new QuickPanelMouseGestureHook(() => settings, () => false,
            _ => 0, (_, _, _, _) => { policyCalls++; return true; }, (button, complete) => replayed.Add((button, complete)));

        Assert.IsTrue(hook.Process(0x0204, 100, 100, 1000), "The hook owns the right-button down while checking for a hold gesture.");
        Assert.IsTrue(hook.Process(0x0205, 100, 100, 1010), "The short press is consumed after replaying the original click.");

        Assert.AreEqual(0, policyCalls, "An ordinary short press must not query process or fullscreen policy.");
        CollectionAssert.AreEqual(new[] { (QuickPanelWakeButton.Right, true) }, replayed);
    }

    [TestMethod]
    public void ShortRightClickQueuesTheCompleteReplayInsteadOfRunningItInline()
    {
        var deferred = new List<QuickPanelWakeButton>();
        var replayed = new List<(QuickPanelWakeButton Button, bool CompleteClick)>();
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelWakeBindings =
        [new QuickPanelWakeBinding { Id = "right-hold", Kind = QuickPanelWakeKind.Hold, Button = QuickPanelWakeButton.Right, HoldMilliseconds = 2000 }];
        using var hook = new QuickPanelMouseGestureHook(() => settings, () => false,
            _ => 0, (_, _, _, _) => true, (button, complete) => replayed.Add((button, complete)), deferred.Add);

        Assert.IsTrue(hook.Process(0x0204, 100, 100, 1000));
        Assert.IsTrue(hook.Process(0x0205, 100, 100, 1010));

        CollectionAssert.AreEqual(new[] { QuickPanelWakeButton.Right }, deferred);
        Assert.AreEqual(0, replayed.Count, "SendInput must be scheduled outside the low-level hook callback.");
    }

    [TestMethod]
    public void MovingPastHoldToleranceReturnsTheOriginalButtonToTheSystem()
    {
        var policyCalls = 0;
        var replayed = new List<(QuickPanelWakeButton Button, bool CompleteClick)>();
        var deferred = new List<QuickPanelWakeButton>();
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelWakeBindings =
        [new QuickPanelWakeBinding { Id = "right-hold", Kind = QuickPanelWakeKind.Hold, Button = QuickPanelWakeButton.Right, HoldMilliseconds = 2000, MovementTolerance = 6 }];
        using var hook = new QuickPanelMouseGestureHook(() => settings, () => false,
            _ => 0, (_, _, _, _) => { policyCalls++; return true; },
            (button, complete) => replayed.Add((button, complete)), deferred.Add);

        Assert.IsTrue(hook.Process(0x0204, 100, 100, 1000));
        Assert.IsFalse(hook.Process(0x0200, 110, 100, 1010), "The move that cancels the gesture continues to the system.");
        Assert.IsFalse(hook.Process(0x0205, 110, 100, 1020), "The matching up continues to the system.");

        Assert.AreEqual(0, policyCalls);
        CollectionAssert.AreEqual(Array.Empty<QuickPanelWakeButton>(), deferred,
            "The movement-cancel DOWN must be replayed synchronously before the move and matching UP pass through.");
        CollectionAssert.AreEqual(new[] { (QuickPanelWakeButton.Right, false) }, replayed,
            "After the hook withheld the original down, it must replay that down once before passing later mouse input through.");
    }

    [TestMethod]
    public void LongHoldFiresOnceAndSuppressesOnlyItsOwnReleaseIncludingTickRollover()
    {
        var detector = new QuickPanelMouseGestureDetector();
        detector.BeginHold(-1000, 100, uint.MaxValue - 299, 600, 6);
        Assert.IsTrue(detector.TryFireHold(300)); Assert.IsFalse(detector.TryFireHold(900));
        detector.CancelPendingHold();
        Assert.IsFalse(detector.ReturnPressToSystem(), "A successful hold must never deliver DOWN to Explorer.");
        Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.Consume, detector.Release());
        Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.PassThrough, detector.Release());
        Assert.IsFalse(detector.OwnsPress);
    }
    [TestMethod]
    public void CancelBeforeThresholdDoesNotConsumeClick()
    {
        var detector = new QuickPanelMouseGestureDetector(); detector.BeginHold(0, 0, 1, 600, 6);
        detector.CancelPendingHold(); Assert.IsFalse(detector.TryFireHold(1000));
        Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.ReplayClick, detector.Release());
    }
    [TestMethod]
    public void RepeatedHoldsAndInterveningShortClicksDoNotLeakButtonOwnership()
    {
        var detector = new QuickPanelMouseGestureDetector();
        for (uint i = 0; i < 20; i++)
        {
            detector.BeginHold(0, 0, i * 2000, 600, 6);
            Assert.IsTrue(detector.TryFireHold(i * 2000 + 600));
            detector.Move(100, 100);
            Assert.IsFalse(detector.ReturnPressToSystem());
            Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.Consume, detector.Release());
            detector.BeginHold(0, 0, i * 2000 + 1000, 600, 6);
            Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.ReplayClick, detector.Release());
            Assert.IsFalse(detector.OwnsPress);
            Assert.IsFalse(detector.TryFireHold(i * 2000 + 1900));
        }
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

    [TestMethod]
    public void DoubleTapKeyIsValidWakeBindingAndTriggersOnlyAfterTwoReleasedPresses()
    {
        var binding = new QuickPanelWakeBinding { Id = "double-f2", Kind = QuickPanelWakeKind.Keyboard, Key = "DoubleTap:F2" };
        Assert.IsNull(QuickPanelWakeBindings.Validate(binding));
        Assert.IsTrue(QuickPanelWakeBindings.SameKey("DoubleTap:F2", "DoubleTap:F2"));
        Assert.IsFalse(QuickPanelWakeBindings.SameKey("DoubleTap:F2", "Ctrl+F2"));

        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelWakeBindings = [binding];
        var detector = new GlobalHotkeyDetector(settings, new Core.Hook.ExplorerTracker());
        detector.OnKeyDown(0x71);
        Assert.IsFalse(detector.CheckQuickPanelHotkey(0x71, out var consume, 100));
        Assert.IsFalse(consume);
        detector.OnKeyUp(0x71);
        detector.OnKeyDown(0x71);
        Assert.IsTrue(detector.CheckQuickPanelHotkey(0x71, out consume, 250));
        Assert.IsTrue(consume);
    }
}
