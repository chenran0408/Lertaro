using System.Windows.Input;
using Lertaro.App.Views.SearchWindow;

namespace Lertaro.App.Tests.Views.SearchWindow;

[TestClass]
public sealed class SearchWindowFilterShortcutTests
{
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
