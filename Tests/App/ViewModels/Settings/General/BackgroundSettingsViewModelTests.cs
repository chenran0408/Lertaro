using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass, DoNotParallelize]
public sealed class BackgroundSettingsViewModelTests
{
    [TestCleanup] public void Cleanup() => BackgroundManager.EndPreview();

    [TestMethod]
    public void PreviewAndCancelDoNotSaveImageChanges()
    {
        var settings = new UserSettings();
        var vm = new BackgroundSettingsViewModel(settings) { UseImage = true, Opacity = 75, FitImage = true };
        Assert.AreEqual(BackgroundKind.Theme, settings.Background.Kind);
        Assert.AreEqual(.75, BackgroundManager.Current.Opacity);
        vm.CancelCommand.Execute(null);
        Assert.IsFalse(vm.UseImage);
        Assert.AreEqual(30d, vm.Opacity);
        vm.Cleanup();
    }

    [TestMethod]
    public void ApplyThenEditAndCloseKeepsAppliedOpacityAndFit()
    {
        var settings = new UserSettings();
        var vm = new BackgroundSettingsViewModel(settings) { UseImage = true, Opacity = 80, FitImage = true };
        vm.Stage(); vm.Accept();
        vm.Opacity = 10; vm.FitImage = false;
        vm.Cleanup();
        Assert.AreEqual(BackgroundKind.Image, settings.Background.Kind);
        Assert.AreEqual(.8, settings.Background.Opacity);
        Assert.IsTrue(settings.Background.FitImage);
    }

    [TestMethod]
    public void FailedSaveAndResetRestoreCommittedChoice()
    {
        var settings = new UserSettings { Background = new() { Kind = BackgroundKind.Image, Opacity = .45 } };
        var vm = new BackgroundSettingsViewModel(settings);
        vm.ResetCommand.Execute(null); vm.Stage(); vm.RejectSave(); vm.Cleanup();
        Assert.AreEqual(.45, settings.Background.Opacity);
        Assert.AreEqual(BackgroundKind.Image, settings.Background.Kind);
    }
}
