using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.App.Views.Settings;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass, DoNotParallelize]
public sealed class BackgroundSettingsSectionTests
{
    [TestCleanup] public void Cleanup() => BackgroundManager.EndPreview();

    [StaTestMethod]
    [DataRow("Light", false)]
    [DataRow("Dark", true)]
    public void ImageToggleAndOpacitySliderPreviewImmediatelyAndKeepTheirBindings(string theme, bool dark)
    {
        var vm = new BackgroundSettingsViewModel(new UserSettings());
        var page = new BackgroundSettingsSection { DataContext = vm };
        page.Resources.MergedDictionaries.Add(new BuiltInTheme(theme, dark).GetResources());
        var window = new Window { Content = page, Width = 650, Height = 750, ShowActivated = false };
        try
        {
            window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var toggle = (CheckBox)page.FindName("RowBackgroundImage");
            var slider = (Slider)page.FindName("RowBackgroundOpacity");
            Assert.IsFalse(slider.IsEnabled);
            toggle.IsChecked = true;
            Assert.IsTrue(vm.UseImage); Assert.IsTrue(slider.IsEnabled);
            slider.Value = 75;
            Assert.AreEqual(.75, BackgroundManager.Current.Opacity);
            var track = Assert.IsInstanceOfType<Track>(slider.Template.FindName("PART_Track", slider));
            Slider.IncreaseSmall.Execute(null, slider);
            Assert.AreEqual(76d, vm.Opacity); Assert.AreEqual(76d, track.Value);
            Assert.IsTrue(System.Windows.Data.BindingOperations.IsDataBound(slider, RangeBase.ValueProperty));
            Assert.AreSame(page.FindResource("AccentBlue"), slider.Foreground);
            Assert.IsLessThanOrEqualTo(650d, page.DesiredSize.Width);
        }
        finally { window.Close(); vm.Cleanup(); }
    }
}
