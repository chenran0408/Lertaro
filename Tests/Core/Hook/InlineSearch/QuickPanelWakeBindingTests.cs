using System.Diagnostics;
using System.Text.Json;
using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass, DoNotParallelize]
public sealed class QuickPanelWakeBindingTests
{
    [TestMethod]
    public void LegacyMigrationPreservesBothKeysDisabledGesturesAndHoldTimeWithoutMutatingSettings()
    {
        var settings = new HotkeyPageSettings { QuickPanelAlternateHotkey = "Ctrl+Alt+F8", QuickPanelMouseRightHold = true,
            QuickPanelRightHoldMilliseconds = 900, QuickPanelMouseCircle = false };
        var migrated = QuickPanelWakeBindings.Get(settings);
        Assert.HasCount(4, migrated);
        Assert.AreEqual("Ctrl+F2", migrated[0].Key); Assert.AreEqual("Ctrl+Alt+F8", migrated[1].Key);
        Assert.IsTrue(migrated[2].Enabled); Assert.AreEqual(900, migrated[2].HoldMilliseconds); Assert.IsFalse(migrated[3].Enabled);
        Assert.IsNull(settings.QuickPanelWakeBindings);
        settings.QuickPanelWakeBindings = [];
        Assert.IsEmpty(QuickPanelWakeBindings.Get(settings), "Removing all rules must not revive Ctrl+F2 from the legacy field.");
    }
    [TestMethod]
    public void AnyNumberOfKeyboardRulesCanMatchAndDisabledOrRemovedKeysStayDisabled()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelWakeBindings = Enumerable.Range(1, 8).Select(i => new QuickPanelWakeBinding
            { Key = $"Ctrl+F{i}", Enabled = i != 4 }).ToList();
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker()); detector.OnKeyDown(0xA2);
        for (var i = 1; i <= 8; i++)
        {
            Assert.AreEqual(i != 4, detector.CheckQuickPanelHotkey(0x6F + i, out var consume));
            Assert.AreEqual(i != 4, consume);
        }
        Assert.IsFalse(detector.CheckQuickPanelHotkey(0x78, out _));
        detector.OnKeyDown(0x71); Assert.IsFalse(detector.QuickPanelKeyRepeated);
        detector.OnKeyDown(0x71); Assert.IsTrue(detector.QuickPanelKeyRepeated);
        detector.OnKeyUp(0x71); detector.OnKeyDown(0x71); Assert.IsFalse(detector.QuickPanelKeyRepeated);
    }
    [TestMethod]
    public void ConflictsCoverHoldVersusClickAndOppositeCircleDirectionsCanCoexist()
    {
        Assert.IsTrue(QuickPanelWakeBindings.SameKey("Alt+Ctrl+Return", "ctrl+alt+Enter"));
        Assert.IsTrue(QuickPanelWakeBindings.SameKey("DoubleTap:F2", "F2"));
        Assert.IsTrue(QuickPanelWakeBindings.SameKey("F2", "DoubleTap:F2"));
        Assert.IsFalse(QuickPanelWakeBindings.SameKey("DoubleTap:F2", "Ctrl+F2"));
        Assert.IsTrue(QuickPanelWakeBindings.SameKey("Ctrl+F2", "CONTROL+F2"));
        Assert.IsTrue(QuickPanelWakeBindings.SameKey("Win+F2", "Windows+F2"));
        var click = new QuickPanelWakeBinding { Kind = QuickPanelWakeKind.Click, Button = QuickPanelWakeButton.Middle };
        var hold = new QuickPanelWakeBinding { Kind = QuickPanelWakeKind.Hold, Button = QuickPanelWakeButton.Middle };
        Assert.IsTrue(QuickPanelWakeBindings.Conflicts(click, hold));
        hold.Enabled = false; Assert.IsFalse(QuickPanelWakeBindings.Conflicts(click, hold));
        var clockwise = new QuickPanelWakeBinding { Kind = QuickPanelWakeKind.Circle, CircleDirection = QuickPanelCircleDirection.Clockwise };
        var counter = new QuickPanelWakeBinding { Kind = QuickPanelWakeKind.Circle, CircleDirection = QuickPanelCircleDirection.Counterclockwise };
        Assert.IsFalse(QuickPanelWakeBindings.Conflicts(clockwise, counter));
        counter.CircleDirection = QuickPanelCircleDirection.Either;
        Assert.IsTrue(QuickPanelWakeBindings.Conflicts(clockwise, counter));
    }
    [TestMethod]
    [DataRow("F2")][DataRow("Ctrl")][DataRow("Bogus+F2")][DataRow("Ctrl+F12")][DataRow("Win+L")]
    public void MalformedOrReservedKeysAreRejected(string key) =>
        Assert.IsNotNull(QuickPanelWakeBindings.Validate(new() { Key = key }));

    [TestMethod]
    [DataRow(1, 1d, QuickPanelCircleDirection.Clockwise, true)]
    [DataRow(1, -1d, QuickPanelCircleDirection.Clockwise, false)]
    [DataRow(3, -1d, QuickPanelCircleDirection.Counterclockwise, true)]
    [DataRow(3, 1d, QuickPanelCircleDirection.Counterclockwise, false)]
    public void CircleSettingsHonorDirectionAndOneOrThreeTurns(int turns, double rotation, QuickPanelCircleDirection direction, bool expected)
    {
        var detector = new QuickPanelMouseGestureDetector(); var recognized = false;
        for (var i = 0; i <= 48 * turns; i++)
        {
            var angle = rotation * i * Math.Tau / 48;
            recognized |= detector.AddCirclePoint((int)(70 * Math.Cos(angle)), (int)(70 * Math.Sin(angle)), (uint)(1000 + i * 18),
                turns: turns, direction: direction);
        }
        Assert.AreEqual(expected, recognized);
    }
    [TestMethod]
    public void RadiusLimitsRejectTooSmallOrLargeCirclesAndClicksRequireNoMovement()
    {
        foreach (var radius in new[] { 15, 260 })
        {
            var detector = new QuickPanelMouseGestureDetector();
            for (var i = 0; i <= 96; i++)
                Assert.IsFalse(detector.AddCirclePoint((int)(radius * Math.Cos(i * Math.Tau / 48)),
                    (int)(radius * Math.Sin(i * Math.Tau / 48)), (uint)(1000 + i * 18)));
        }
        var press = new QuickPanelMouseGestureDetector(); press.BeginHold(0, 0, 1, 600, 6);
        Assert.IsTrue(press.TryFireClick()); Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.Consume, press.Release());
        press.BeginHold(0, 0, 1, 600, 6); press.Move(20, 0);
        Assert.IsFalse(press.TryFireClick()); Assert.IsTrue(press.ReturnPressToSystem());
        Assert.AreEqual(QuickPanelMouseGestureDetector.ReleaseAction.PassThrough, press.Release());
    }
    [TestMethod]
    public void TestSnapshotExpiresAndInvalidPayloadNeverReplacesAValidSession()
    {
        try
        {
            var request = new QuickPanelWakeTestRequest { Binding = new() { Kind = QuickPanelWakeKind.Hold, HoldMilliseconds = 900 } };
            Assert.IsTrue(QuickPanelWakeTestSession.Start(JsonSerializer.Serialize(request)));
            var session = QuickPanelWakeTestSession.Current!;
            request.Binding.HoldMilliseconds = 1500;
            Assert.AreEqual(900, session.Request.Binding.HoldMilliseconds);
            Assert.IsFalse(QuickPanelWakeTestSession.Start("{"));
            Assert.AreSame(session, QuickPanelWakeTestSession.Current);
            var clock = new QuickPanelWakeTestSession(session.Request, 1000);
            Assert.IsTrue(clock.IsActive(1000 + Stopwatch.Frequency * 29));
            Assert.IsFalse(clock.IsActive(1000 + Stopwatch.Frequency * 30));
            QuickPanelWakeTestSession.Stop(); Assert.IsNull(QuickPanelWakeTestSession.Current);
        }
        finally { QuickPanelWakeTestSession.Stop(); }
    }
    [TestMethod]
    public void TestScopeChangesDoNotOverrideBlacklistOrPauseAndKeyboardIgnoresMouseScope()
    {
        var settings = new UserSettings(); var request = new QuickPanelWakeTestRequest { AllowDesktop = false };
        Assert.AreEqual("Wake_TestLocation", QuickPanelMouseWakePolicy.Reason(settings, false, true, "explorer", "explorer", false, request));
        Assert.IsNull(QuickPanelMouseWakePolicy.Reason(settings, false, true, "explorer", "explorer", false, request, keyboard: true));
        settings.BlacklistedProcesses.Add("explorer");
        Assert.AreEqual("Wake_TestBlacklisted", QuickPanelMouseWakePolicy.Reason(settings, false, true, "explorer", "explorer", false, request));
        Assert.AreEqual("Wake_TestPaused", QuickPanelMouseWakePolicy.Reason(settings, true, true, "explorer", "explorer", false, request));
    }
}
