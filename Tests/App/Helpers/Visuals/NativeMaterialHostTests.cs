using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.Theme;
using Lertaro.Core;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.App.Views.QuickPanel;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass, DoNotParallelize]
public sealed class NativeMaterialHostTests
{
    [TestInitialize] public void Initialize() => BackgroundManager.Preview(new());
    [TestCleanup] public void Cleanup() => BackgroundManager.EndPreview();

    [StaTestMethod]
    public void OrdinaryBackgroundKeepsExistingTransparencyAndInsets()
    {
        var border = new Border { Margin = new Thickness(8) };
        var window = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Content = border };
        NativeMaterialHost.Prepare(window);
        Assert.IsTrue(window.AllowsTransparency);
        Assert.AreEqual(new Thickness(8), border.Margin);
        Assert.IsFalse(NativeMaterialHost.Refresh(window));
    }

    [StaTestMethod]
    public void MaterialSelectsTheNativeStructureBeforeCreationAndRemovesShadowPadding()
    {
        BackgroundManager.Preview(new() { Kind = BackgroundKind.Material });
        var border = new Border { Margin = new Thickness(8) };
        var window = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Content = border };
        NativeMaterialHost.Prepare(window);
        Assert.AreEqual(!NativeMaterialHost.IsSupported, window.AllowsTransparency);
        Assert.AreEqual(new Thickness(NativeMaterialHost.IsSupported ? 0 : 8), border.Margin);
    }

    [StaTestMethod]
    public void ChangingToMaterialDoesNotMutateALayeredWindowThatAlreadyExists()
    {
        var window = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Content = new Border() };
        NativeMaterialHost.Prepare(window);
        try
        {
            window.Show();
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Material });
            Assert.IsFalse(NativeMaterialHost.Refresh(window));
            Assert.IsTrue(window.AllowsTransparency);
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public void QuickPanelUsesNativeInsetsAndCanReturnToAnOrdinaryBackground()
    {
        BackgroundManager.Preview(new() { Kind = BackgroundKind.Material });
        var vm = new QuickPanelViewModel(() => new(), (_, _) => Task.FromResult(new List<SearchResult>()), () => { });
        var window = new QuickPanelWindow(vm) { Width = 600, Height = 400 };
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(NativeMaterialHost.IsSupported, NativeMaterialHost.Refresh(window));
            Assert.AreEqual(new Thickness(NativeMaterialHost.IsSupported ? 0 : 8), window.VisibleContentInset);
            Assert.AreEqual(1d, ((FrameworkElement)window.Content).Opacity);
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Gradient });
            Assert.IsFalse(NativeMaterialHost.Refresh(window));
        }
        finally { window.Close(); }
    }
}
