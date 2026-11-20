using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass, DoNotParallelize]
public sealed class ThemeSettingsViewModelTests
{
    [STATestMethod]
    public void PreviewOnlyPersistsAfterSuccessfulSave()
    {
        var settings = new UserSettings { Theme = "Light", ThemeFollowSystem = false };
        var vm = new ThemeSettingsViewModel(settings);
        try
        {
            vm.ThemeModeTag = "Dark";
            Assert.AreEqual("Light", settings.Theme);
            vm.Stage();
            Assert.AreEqual("Dark", settings.Theme);
            vm.RejectSave();
            Assert.AreEqual("Light", settings.Theme);
            vm.Stage();
            vm.Accept();
            vm.ThemeModeTag = "FollowSystem";
        }
        finally { vm.Cleanup(); }
        Assert.AreEqual("Dark", settings.Theme);
        Assert.IsFalse(settings.ThemeFollowSystem);
    }
}
