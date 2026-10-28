using System.Windows;
using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

[TestClass, DoNotParallelize]
public sealed class QuickSearchWindowStartupVisibilityTests
{
    [STATestMethod]
    public void StartupSeedIsTransparentUntilHiddenAndRestoredForLaterSummons()
    {
        var window = new Window();
        try
        {
            QuickSearchWindowLifecycle.PrepareHiddenStartup(window);
            Assert.AreEqual(0d, window.Opacity);

            window.Show();
            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(0d, window.Opacity, "Show must not expose the startup seed.");

            QuickSearchWindowLifecycle.CompleteHiddenStartup(window);
            Assert.IsFalse(window.IsVisible);
            Assert.AreEqual(1d, window.Opacity, "A later summon must use normal opacity.");

            window.Show();
            Assert.IsTrue(window.IsVisible);
            Assert.AreEqual(1d, window.Opacity);
        }
        finally
        {
            window.Close();
        }
    }
}
