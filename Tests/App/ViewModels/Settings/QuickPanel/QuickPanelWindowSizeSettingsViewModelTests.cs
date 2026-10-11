using System.Text.Json;
using Lertaro.App.Services.QuickPanel;
using Lertaro.App.ViewModels.Settings.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.QuickPanel;

[TestClass]
public sealed class QuickPanelWindowSizeSettingsViewModelTests
{
    [TestMethod]
    public void EditsAreStagedUntilTheParentSaves()
    {
        var settings = new UserSettings();
        var parent = new QuickPanelSettingsViewModel(settings);
        parent.WindowSize.UseCustomWindowSize = true;
        parent.WindowSize.Width = 900;
        parent.WindowSize.Height = 700;
        Assert.IsFalse(settings.QuickPanel.UseCustomWindowSize);
        Assert.AreEqual(640d, settings.QuickPanel.WindowWidth);
        parent.Save();
        Assert.IsTrue(settings.QuickPanel.UseCustomWindowSize);
        Assert.AreEqual(900d, settings.QuickPanel.WindowWidth);
        Assert.AreEqual(700d, settings.QuickPanel.WindowHeight);
    }

    [TestMethod]
    public void InvalidAndExtremeDimensionsAreNormalized()
    {
        var model = new QuickPanelWindowSizeSettingsViewModel(new QuickPanelSettings
        {
            WindowWidth = double.NaN,
            WindowHeight = double.PositiveInfinity
        });
        Assert.AreEqual(640d, model.Width);
        Assert.AreEqual(480d, model.Height);
        model.Width = -1;
        model.Height = 99999;
        Assert.AreEqual(280d, model.Width);
        Assert.AreEqual(4096d, model.Height);
    }

    [TestMethod]
    public void OldSettingsRemainAutomaticAndCustomSizeRoundTrips()
    {
        var settings = JsonSerializer.Deserialize<QuickPanelSettings>("{}");
        Assert.IsNotNull(settings);
        Assert.IsFalse(settings.UseCustomWindowSize);
        settings.UseCustomWindowSize = true;
        settings.WindowWidth = 800;
        settings.WindowHeight = 600;
        var restored = JsonSerializer.Deserialize<QuickPanelSettings>(JsonSerializer.Serialize(settings));
        Assert.IsNotNull(restored);
        Assert.IsTrue(restored.UseCustomWindowSize);
        Assert.AreEqual(800d, restored.WindowWidth);
        Assert.AreEqual(600d, restored.WindowHeight);
    }

    [TestMethod]
    public void CustomSizeUsesHostDpiAndFitsNegativeCoordinateMonitor()
    {
        var (width, height, left, top) = QuickPanelManager.CalculatePhysicalDockPosition(
            -1920, 0, 0, 1080, 144, -1920, 0, 1920, 1080, 800, 600);
        Assert.AreEqual(800d, width);
        Assert.AreEqual(600d, height);
        Assert.AreEqual(-1218d, left);
        Assert.AreEqual(162d, top);
        (width, height, left, top) = QuickPanelManager.CalculatePhysicalDockPosition(
            -1920, 0, 0, 1080, 144, -1920, 0, 1920, 1080, 4096, 4096);
        Assert.AreEqual(1280d, width);
        Assert.AreEqual(720d, height);
        Assert.AreEqual(-1920d, left);
        Assert.AreEqual(0d, top);
    }

    [TestMethod]
    public void InvalidCustomSizeFallsBackToAutomaticGeometry()
    {
        var expected = QuickPanelManager.CalculatePhysicalDockPosition(100, 100, 900, 700, 96, 0, 0, 1920, 1080);
        var actual = QuickPanelManager.CalculatePhysicalDockPosition(100, 100, 900, 700, 96, 0, 0, 1920, 1080, double.NaN, double.PositiveInfinity);
        Assert.AreEqual(expected, actual);
    }

    [StaTestMethod]
    public void PreviewTracksEditsWithoutSavingThem()
    {
        var settings = new QuickPanelSettings();
        var model = new QuickPanelWindowSizeSettingsViewModel(settings);
        var preview = new Lertaro.App.Views.Settings.QuickPanel.QuickPanelSizePreviewWindow { DataContext = model };
        try
        {
            preview.Show();
            preview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            model.Width = 850;
            model.Height = 650;
            preview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            preview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(850d, preview.Width, 1);
            Assert.AreEqual(850d, preview.ActualWidth, 1);
            Assert.AreEqual(650d, preview.Height, 1);
            Assert.AreEqual(650d, preview.ActualHeight, 1);
            model.Width = 500;
            model.Height = 350;
            preview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(500d, preview.ActualWidth, 1);
            Assert.AreEqual(350d, preview.ActualHeight, 1);
            Assert.AreEqual(640d, settings.WindowWidth);
            Assert.AreEqual(480d, settings.WindowHeight);
        }
        finally { preview.Close(); }
    }
}
