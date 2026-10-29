using Lertaro.App.Helpers;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Interop;

namespace Lertaro.App.Tests.Helpers;

// The gate both search windows share for the "keep search box content" setting. Only the decision is
// covered: selecting needs a live window, so both windows call this and then act on it.
[TestClass, DoNotParallelize]
public sealed class SearchInputHelperTests
{
    [TestMethod]
    public void CarriedTextIsSelectedWhenTheSettingIsOn() =>
        // What the full window does with the query it was handed, and the quick window with what it kept.
        Assert.IsTrue(SearchInputHelper.ShouldSelectCarriedText("report", keepSearchText: true));

    [TestMethod]
    public void NothingIsSelectedWhenTheSettingIsOff() =>
        // Off is the old behaviour: the full window parks the caret after the text so typing appends.
        Assert.IsFalse(SearchInputHelper.ShouldSelectCarriedText("report", keepSearchText: false));

    [TestMethod]
    public void AnEmptyBoxHasNothingToSelect() =>
        // The ordinary "open the full window with no query" case, which must not change.
        Assert.IsFalse(SearchInputHelper.ShouldSelectCarriedText("", keepSearchText: true));

    [TestMethod]
    public void ANullBoxHasNothingToSelect() =>
        Assert.IsFalse(SearchInputHelper.ShouldSelectCarriedText(null, keepSearchText: true));

    [TestMethod]
    public void PendingSearchActionAcceptsOnlyAnUnmodifiedSameKeyWithinTheDoubleTapWindow()
    {
        Assert.IsTrue(SearchInputHelper.IsSecondTap(Key.A, false, ModifierKeys.None, 1310, Key.A, 1000));
        Assert.IsFalse(SearchInputHelper.IsSecondTap(Key.B, false, ModifierKeys.None, 1100, Key.A, 1000));
        Assert.IsFalse(SearchInputHelper.IsSecondTap(Key.A, false, ModifierKeys.Control, 1100, Key.A, 1000));
        Assert.IsFalse(SearchInputHelper.IsSecondTap(Key.A, true, ModifierKeys.None, 1100, Key.A, 1000));
        Assert.IsFalse(SearchInputHelper.IsSecondTap(Key.A, false, ModifierKeys.None, 1321, Key.A, 1000));
        Assert.IsTrue(SearchInputHelper.IsSecondTap(Key.A, false, ModifierKeys.None, 12, Key.A, uint.MaxValue - 100));
    }

    [STATestMethod]
    public void DeferredEnterDoubleTapIsArmedOnFirstTapAndRunsOnceOnSecond()
    {
        using var source = new HwndSource(new HwndSourceParameters("DeferredDoubleTapEnterTest"));
        KeyEventArgs Press(Key key = Key.Return) => new(Keyboard.PrimaryDevice, source, Environment.TickCount, key);

        SearchInputHelper.ArmDoubleTapActionMatchers(Press(), ["DoubleTap:Enter"]);

        var actionCalls = 0;
        var defaultEnterCalls = 0;
        var secondTapMatched = SearchInputHelper.ExecuteDeferredAction(
            () => WpfUiHelper.MatchesHotkey("DoubleTap:Enter", Press()) && ++actionCalls == 1,
            () => defaultEnterCalls++);

        Assert.IsTrue(secondTapMatched);
        Assert.AreEqual(1, actionCalls);
        Assert.AreEqual(0, defaultEnterCalls);
        Assert.IsFalse(WpfUiHelper.MatchesHotkey("DoubleTap:Enter", Press()), "A completed pair must not execute again.");
        WpfUiHelper.MatchesHotkey("Ctrl+F8", Press(Key.F8));
    }

    [TestMethod]
    public void DeferredEnterRunsWhenTheDoubleTapActionCannotExecute()
    {
        var defaultEnterCalls = 0;

        var actionExecuted = SearchInputHelper.ExecuteDeferredAction(() => false, () => defaultEnterCalls++);

        Assert.IsFalse(actionExecuted);
        Assert.AreEqual(1, defaultEnterCalls);
    }

    [STATestMethod]
    public void ActionsMnemonic_PassesImeLettersToFilterAndStillMatchesPlainKeys()
    {
        using var source = new HwndSource(new HwndSourceParameters("ActionsMnemonicImeTest"));
        var item = new ActionMenuItem { Text = "Open", ShortcutHint = "N" };
        var items = new[] { item };
        var imeKey = ImeProcessed(source, Key.N);

        Assert.AreEqual(Key.N, WpfUiHelper.GetActualKey(imeKey));
        Assert.IsTrue(WpfUiHelper.MatchesHotkey("Ctrl+N", ModifierKeys.Control, WpfUiHelper.GetActualKey(imeKey)));
        Assert.IsFalse(SearchInputHelper.TryGetActionsMenuMnemonic(imeKey, ModifierKeys.None, string.Empty, items, out var imeMatch));
        Assert.IsNull(imeMatch);

        var plainKey = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.N)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        Assert.IsTrue(SearchInputHelper.TryGetActionsMenuMnemonic(plainKey, ModifierKeys.None, string.Empty, items, out var plainMatch));
        Assert.AreSame(item, plainMatch);
        Assert.IsFalse(SearchInputHelper.TryGetActionsMenuMnemonic(plainKey, ModifierKeys.Control, string.Empty, items, out _));
        Assert.IsFalse(SearchInputHelper.TryGetActionsMenuMnemonic(plainKey, ModifierKeys.None, "n", items, out _));
    }

    private static KeyEventArgs ImeProcessed(HwndSource source, Key actualKey)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.ImeProcessed)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        var realKey = typeof(KeyEventArgs).GetField("_realKey", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(KeyEventArgs).FullName, "_realKey");
        realKey.SetValue(e, actualKey);
        return e;
    }
}
