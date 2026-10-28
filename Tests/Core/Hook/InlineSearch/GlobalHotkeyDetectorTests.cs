using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class GlobalHotkeyDetectorTests
{
    [TestMethod]
    public void QuickPanelHotkey_RemainsAvailableAfterAnotherCtrlChord()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+F2";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        Assert.IsFalse(detector.CheckQuickPanelHotkey(0x4E, out _));
        detector.OnKeyUp(0x4E);
        detector.OnKeyUp(0xA2);

        detector.OnKeyDown(0xA2);
        var triggered = detector.CheckQuickPanelHotkey(0x71, out var consumeKey);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    [TestMethod]
    public void ToggleHotkey_RecognizesTrackedAltState()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "Alt+Space";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA4);
        var triggered = detector.CheckToggleWindowHotkey(0x20, 1000, out var consumeKey, null);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    [TestMethod]
    public void GlobalToggleAndQuickNavigationAcceptDoubleTapOfAnyKey()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "DoubleTap:F2";
        settings.Hotkeys.QuickNavigationHotkey = "DoubleTap:F3";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0x71);
        Assert.IsFalse(detector.CheckToggleWindowHotkey(0x71, 100, out var consumeToggle, null));
        Assert.IsFalse(consumeToggle);
        detector.OnKeyUp(0x71);
        detector.OnKeyDown(0x71);
        Assert.IsTrue(detector.CheckToggleWindowHotkey(0x71, 250, out consumeToggle, null));
        Assert.IsTrue(consumeToggle);
        detector.OnKeyUp(0x71);

        detector.OnKeyDown(0x72);
        Assert.IsFalse(detector.CheckQuickNavigationHotkey(0x72, out var consumeNavigation, 500));
        Assert.IsFalse(consumeNavigation);
        detector.OnKeyUp(0x72);
        detector.OnKeyDown(0x72);
        Assert.IsTrue(detector.CheckQuickNavigationHotkey(0x72, out consumeNavigation, 650));
        Assert.IsTrue(consumeNavigation);
    }

    [TestMethod]
    public void FavoriteDoubleTapPassesFirstKeyAndMatchesSecondReleasedPress()
    {
        var settings = new UserSettings();
        settings.Favorites = [new FavoriteItemSetting { Hotkey = "DoubleTap:F2" }];
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0x71);
        Assert.IsFalse(detector.CheckFavoriteDoubleTap(0x71, 100, out _));
        detector.OnKeyUp(0x71);
        detector.OnKeyDown(0x71);
        Assert.IsTrue(detector.CheckFavoriteDoubleTap(0x71, 250, out var ownerIndex));
        Assert.AreEqual(0, ownerIndex);
    }

    [TestMethod]
    public void QuickPanelDoubleTap_OtherKeyBetweenTapsStartsANewSequence()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "DoubleTap:F8";
        settings.Hotkeys.QuickPanelWakeBindings = [new() { Id = "double-tap", Kind = QuickPanelWakeKind.Keyboard, Key = "DoubleTap:F8" }];
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0x77);
        Assert.IsFalse(detector.CheckQuickPanelHotkey(0x77, out _, 100));
        detector.OnKeyUp(0x77);
        detector.OnKeyDown(0x78);
        detector.OnKeyUp(0x78);
        detector.OnKeyDown(0x77);
        Assert.IsFalse(detector.CheckQuickPanelHotkey(0x77, out _, 200));
        detector.OnKeyUp(0x77);
        detector.OnKeyDown(0x77);
        Assert.IsTrue(detector.CheckQuickPanelHotkey(0x77, out _, 250));
    }

    [TestMethod]
    public void FavoriteDoubleTapCollisionLeavesTheHostDoubleTapInControl()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "DoubleTap:F2";
        settings.Favorites = [new FavoriteItemSetting { Hotkey = "DoubleTap:F2" }];
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0x71);
        Assert.IsFalse(detector.CheckFavoriteDoubleTap(0x71, 100, out _));
        Assert.IsFalse(detector.CheckToggleWindowHotkey(0x71, 100, out _, null));
        detector.OnKeyUp(0x71);
        detector.OnKeyDown(0x71);
        Assert.IsFalse(detector.CheckFavoriteDoubleTap(0x71, 250, out _));
        Assert.IsTrue(detector.CheckToggleWindowHotkey(0x71, 250, out var consumeKey, null));
        Assert.IsTrue(consumeKey);
    }

    [TestMethod]
    public void ReservedDoubleTapWinIsNeverMatchedAsAFavorite()
    {
        var settings = new UserSettings();
        settings.Favorites = [new FavoriteItemSetting { Hotkey = "DoubleTap:Win" }];
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0x5B);
        Assert.IsFalse(detector.CheckFavoriteDoubleTap(0x5B, 100, out _));
        detector.OnKeyUp(0x5B);
        detector.OnKeyDown(0x5B);
        Assert.IsFalse(detector.CheckFavoriteDoubleTap(0x5B, 250, out _));
    }

    [TestMethod]
    public void DuplicateFavoriteDoubleTapRoutesOnlyToTheFirstFavorite()
    {
        var settings = new UserSettings();
        settings.Favorites =
        [
            new FavoriteItemSetting { Hotkey = "DoubleTap:F2" },
            new FavoriteItemSetting { Hotkey = "DoubleTap:F2" }
        ];
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        for (uint start = 100; start < 500; start += 200)
        {
            detector.OnKeyDown(0x71);
            Assert.IsFalse(detector.CheckFavoriteDoubleTap(0x71, start, out _));
            detector.OnKeyUp(0x71);
            detector.OnKeyDown(0x71);
            Assert.IsTrue(detector.CheckFavoriteDoubleTap(0x71, start + 100, out var ownerIndex));
            Assert.AreEqual(0, ownerIndex);
            detector.OnKeyUp(0x71);
        }
    }

    [TestMethod]
    public void QuickPanelHotkey_RecognizesMultipleTrackedModifiers()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+Alt+Shift+Win+F2";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        detector.OnKeyDown(0xA4);
        detector.OnKeyDown(0xA0);
        detector.OnKeyDown(0x5B);

        var triggered = detector.CheckQuickPanelHotkey(0x71, out var consumeKey);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    // The chord predicates the inline window's Alt+Space suppression consults: they must answer for a
    // configured summon chord without consuming it or moving the double-tap detectors' state, because
    // the detection that will consume it runs later on the same keystroke.
    [TestMethod]
    public void ToggleComboPredicate_AnswersTheChordWithoutTouchingTapState()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "Alt+Space";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        Assert.IsFalse(detector.IsToggleWindowComboDown(0x20));
        detector.OnKeyDown(0xA4);
        Assert.IsTrue(detector.IsToggleWindowComboDown(0x20));
        // The modifiers are an exact match: Alt held is the combination, Alt plus anything else is not.
        detector.OnKeyDown(0xA2);
        Assert.IsFalse(detector.IsToggleWindowComboDown(0x20));
        Assert.IsFalse(detector.IsToggleWindowComboDown(0x71));
    }

    [TestMethod]
    public void ToggleComboPredicate_IsFalseForTheBareModifierForm()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "Ctrl";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        Assert.IsFalse(detector.IsToggleWindowComboDown(0x20));
    }

    [TestMethod]
    public void QuickPanelComboPredicate_AnswersItsOwnCombination()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+F2";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        Assert.IsTrue(detector.IsQuickPanelComboDown(0x71));
        Assert.IsFalse(detector.IsQuickPanelComboDown(0x20));
    }
}
