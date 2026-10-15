using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass, DoNotParallelize]
public sealed class BackgroundSettingsViewModelTests
{
    [TestInitialize] public void Initialize() => BackgroundManager.EndPreview();
    [TestCleanup] public void Cleanup() => BackgroundManager.EndPreview();

    [TestMethod]
    public void PreviewDoesNotMutatePersistedPreferencesAndCancelRestoresSavedChoice()
    {
        var settings = new UserSettings { Background = new() { Kind = BackgroundKind.Gradient } };
        var vm = new BackgroundSettingsViewModel(settings);
        vm.Kind = (int)BackgroundKind.Solid;
        vm.FirstColor = "#123456";
        Assert.AreEqual(BackgroundKind.Gradient, settings.Background.Kind);
        Assert.AreEqual("#123456", BackgroundManager.Current.FirstColor);
        vm.CancelCommand.Execute(null);
        Assert.AreEqual((int)BackgroundKind.Gradient, vm.Kind);
    }

    [TestMethod]
    public void ApplyThenFurtherPreviewThenCloseRestoresLastAppliedChoice()
    {
        var settings = new UserSettings();
        var vm = new BackgroundSettingsViewModel(settings) { Kind = (int)BackgroundKind.Gradient, Angle = 250 };
        vm.Stage();
        vm.Accept();
        vm.Angle = 20;
        vm.Kind = (int)BackgroundKind.Solid;
        vm.Cleanup();
        Assert.AreEqual(BackgroundKind.Gradient, settings.Background.Kind);
        Assert.AreEqual(250d, settings.Background.Angle);
    }

    [TestMethod]
    public void FailedSaveStagingDoesNotSurviveCleanup()
    {
        var settings = new UserSettings();
        var vm = new BackgroundSettingsViewModel(settings) { Kind = (int)BackgroundKind.Gradient };
        vm.Stage();
        vm.Cleanup();
        Assert.AreEqual(BackgroundKind.Theme, settings.Background.Kind);
    }
}
