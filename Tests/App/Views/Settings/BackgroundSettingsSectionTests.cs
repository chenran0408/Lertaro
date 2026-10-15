using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.App.Views.Settings;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass, DoNotParallelize]
public sealed class BackgroundSettingsSectionTests
{
    [TestInitialize] public void Initialize() => BackgroundManager.EndPreview();
    [TestCleanup] public void Cleanup() => BackgroundManager.EndPreview();

    [StaTestMethod]
    public void BackgroundControlsLoadAndSwitchWithoutMissingResources()
    {
        var vm = new BackgroundSettingsViewModel(new UserSettings());
        var page = new BackgroundSettingsSection { DataContext = vm };
        page.Resources.MergedDictionaries.Add(new() { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles.xaml") });
        page.Resources.MergedDictionaries.Add(new() { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles/Windows/SettingsWindow.xaml") });
        page.Resources.MergedDictionaries.Add(new BuiltInTheme("Light", false).GetResources());
        var window = new Window { Content = page, Width = 650, Height = 750 };
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var modes = Assert.IsInstanceOfType<ComboBox>(page.FindName("RowBackgroundKind"));
            Assert.AreEqual(0, modes.SelectedIndex);
            modes.SelectedIndex = 2;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(vm.IsGradient);
            Assert.AreEqual(BackgroundKind.Gradient, BackgroundManager.Current.Kind);
            Assert.IsLessThanOrEqualTo(650d, page.DesiredSize.Width);
        }
        finally { window.Close(); vm.Cleanup(); }
    }
}
