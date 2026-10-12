using System.Windows.Input;
using Lertaro.Core;
using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass]
public sealed class QuickSearchLaunchShortcutSupportTests
{
    [TestMethod]
    public void ShortcutKeysMapToExpectedIndices()
    {
        var cases = new[]
        {
            (Key.D1, 0), (Key.NumPad9, 8), (Key.D0, 9),
            (Key.NumPad0, 9), (Key.A, 10), (Key.Z, 35),
        };

        foreach (var (key, expectedIndex) in cases)
            Assert.AreEqual(expectedIndex, QuickSearchLaunchShortcutSupport.GetShortcutIndex(key));
    }

    [TestMethod]
    public void NonShortcutKeyIsRejected() => Assert.AreEqual(-1, QuickSearchLaunchShortcutSupport.GetShortcutIndex(Key.Enter));

    [TestMethod]
    [DataRow(Key.F)]
    [DataRow(Key.S)]
    [DataRow(Key.T)]
    [DataRow(Key.N)]
    [DataRow(Key.P)]
    [DataRow(Key.O)]
    [DataRow(Key.A)]
    [DataRow(Key.C)]
    [DataRow(Key.V)]
    [DataRow(Key.X)]
    [DataRow(Key.Y)]
    [DataRow(Key.Z)]
    public void WindowCommandsAndEditingChordsAreNeverLaunchShortcuts(Key key) =>
        Assert.IsTrue(QuickSearchLaunchShortcutSupport.IsReservedShortcut(key, ModifierKeys.Control, new()));

    [TestMethod]
    public void UnreservedNumbersAndLettersRemainAvailable()
    {
        Assert.IsFalse(QuickSearchLaunchShortcutSupport.IsReservedShortcut(Key.D1, ModifierKeys.Control, new()));
        Assert.IsFalse(QuickSearchLaunchShortcutSupport.IsReservedShortcut(Key.B, ModifierKeys.Control, new()));
    }

    [TestMethod]
    public void ReboundWindowAndPluginChordsRemainReserved()
    {
        var settings = new HotkeyPageSettings { OpenFullWindowHotkey = "Alt+B", PluginActionHotkeys = new() { ["plugin"] = new() { ["action"] = "Ctrl+L" } } };
        Assert.IsTrue(QuickSearchLaunchShortcutSupport.IsReservedShortcut(Key.B, ModifierKeys.Alt, settings));
        Assert.IsTrue(QuickSearchLaunchShortcutSupport.IsReservedShortcut(Key.L, ModifierKeys.Control, settings));
        Assert.IsFalse(QuickSearchLaunchShortcutSupport.IsReservedShortcut(Key.F, ModifierKeys.Control, settings));
    }

    [TestMethod]
    public void LabelsUseDigitsThenZeroThenLetters()
    {
        Assert.AreEqual("Ctrl+1", QuickSearchLaunchShortcutSupport.FormatShortcutHint(0, "Ctrl"));
        Assert.AreEqual("Ctrl+0", QuickSearchLaunchShortcutSupport.FormatShortcutHint(9, "Ctrl"));
        Assert.AreEqual("Ctrl+A", QuickSearchLaunchShortcutSupport.FormatShortcutHint(10, "Ctrl"));
        Assert.AreEqual("Ctrl+Z", QuickSearchLaunchShortcutSupport.FormatShortcutHint(35, "Ctrl"));
    }

    [TestMethod]
    public void NoneModifierOmitsThePrefix() => Assert.AreEqual("A", QuickSearchLaunchShortcutSupport.FormatShortcutHint(10, "None"));

    [TestMethod]
    public void LabelLimitIsThirtySixItems() => Assert.AreEqual(string.Empty, QuickSearchLaunchShortcutSupport.FormatShortcutHint(36, "Ctrl"));

    [TestMethod]
    public void ScrollOffsetSnapsToNearestWholeRowWithinBounds()
    {
        Assert.AreEqual(0, QuickSearchLaunchShortcutSupport.SnapOffsetToRow(40, 400));
        Assert.AreEqual(104, QuickSearchLaunchShortcutSupport.SnapOffsetToRow(70, 400));
        Assert.AreEqual(250, QuickSearchLaunchShortcutSupport.SnapOffsetToRow(500, 250));
    }
}
