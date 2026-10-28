using System.Windows.Input;
using System.Windows.Interop;
using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass, DoNotParallelize]
public sealed class WpfUiHelperDoubleTapTests
{
    [STATestMethod]
    public void RecorderHotkeyMatchesTwoSeparateKeyEvents()
    {
        using var source = new HwndSource(new HwndSourceParameters("DoubleTapTest"));
        KeyEventArgs Press() => new(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.F8);

        Assert.IsFalse(WpfUiHelper.MatchesHotkey("DoubleTap:F8", Press()));
        Assert.IsTrue(WpfUiHelper.MatchesHotkey("DoubleTap:F8", Press()));
        Assert.IsFalse(WpfUiHelper.MatchesHotkey("DoubleTap:F8", Press()), "A completed pair must be consumed before another pair starts.");
    }

    [STATestMethod]
    public void AnotherKeyBetweenTaps_RequiresANewFirstTap()
    {
        using var source = new HwndSource(new HwndSourceParameters("DoubleTapResetTest"));
        KeyEventArgs Press(Key key) => new(Keyboard.PrimaryDevice, source, Environment.TickCount, key);

        Assert.IsFalse(WpfUiHelper.MatchesHotkey("DoubleTap:F7", Press(Key.F7)));
        Assert.IsFalse(WpfUiHelper.MatchesHotkey("DoubleTap:F7", Press(Key.F8)));
        Assert.IsFalse(WpfUiHelper.MatchesHotkey("DoubleTap:F7", Press(Key.F7)));
        Assert.IsTrue(WpfUiHelper.MatchesHotkey("DoubleTap:F7", Press(Key.F7)));
    }

    [STATestMethod]
    public void EnterAlias_TriggersOnTwoReturnKeyEvents()
    {
        using var source = new HwndSource(new HwndSourceParameters("DoubleTapEnterTest"));
        KeyEventArgs Press() => new(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Return);

        Assert.IsFalse(WpfUiHelper.MatchesHotkey("DoubleTap:Enter", Press()));
        Assert.IsTrue(WpfUiHelper.MatchesHotkey("DoubleTap:Enter", Press()));
    }
}
