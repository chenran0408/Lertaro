using System.Windows.Input;
using Lertaro.App.Views.SearchWindow;

namespace Lertaro.App.Tests.Views.SearchWindow;

[TestClass]
public sealed class SearchWindowFilterShortcutTests
{
    [TestMethod]
    [DataRow(800.0, false)]
    [DataRow(999.0, false)]
    [DataRow(1000.0, true)]
    public void FilterPaneOpensByDefaultWhenTheWindowHasRoom(double width, bool expected) =>
        Assert.AreEqual(expected, global::Lertaro.App.SearchWindow.ShouldShowFilterPanel(width, null));

    [TestMethod]
    public void ExplicitFilterChoiceSurvivesResizing()
    {
        Assert.IsTrue(global::Lertaro.App.SearchWindow.ShouldShowFilterPanel(800, true));
        Assert.IsFalse(global::Lertaro.App.SearchWindow.ShouldShowFilterPanel(1200, false));
    }

    [TestMethod]
    public void CtrlShiftF_TogglesFilters()
    {
        Assert.IsTrue(SearchWindowInputHandler.IsFilterToggleShortcut(
            Key.F, ModifierKeys.Control | ModifierKeys.Shift));
    }

    [TestMethod]
    public void PlainF_DoesNotToggleFilters()
    {
        Assert.IsFalse(SearchWindowInputHandler.IsFilterToggleShortcut(Key.F, ModifierKeys.None));
    }

    [TestMethod]
    public void CtrlShiftOtherKey_DoesNotToggleFilters()
    {
        Assert.IsFalse(SearchWindowInputHandler.IsFilterToggleShortcut(
            Key.G, ModifierKeys.Control | ModifierKeys.Shift));
    }
}
