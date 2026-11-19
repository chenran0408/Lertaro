using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class UserSettingsModelsTests
{
    [TestMethod]
    [DataRow("""{}""", true)]
    [DataRow("""{"OnboardingCompleted":true,"OnboardingVersion":0}""", true)]
    [DataRow("""{"OnboardingCompleted":true,"OnboardingVersion":0,"OnboardingSkipStep":2}""", false)]
    [DataRow("""{"OnboardingCompleted":true,"OnboardingVersion":2}""", false)]
    public void OnboardingStartupHonorsAnExplicitSkipFromExistingSettings(string json, bool expected)
    {
        var loaded = JsonSerializer.Deserialize<UserSettings>(json);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(expected, loaded.ShouldShowOnboarding());
    }

    [TestMethod]
    [DataRow("""{}""", true)]
    [DataRow("""{"RelativeLeft":0.2,"RelativeTop":0.4}""", false)]
    [DataRow("""{"FollowCursorOnSummon":true,"RelativeLeft":0.2,"RelativeTop":0.4}""", true)]
    [DataRow("""{"FollowCursorOnSummon":false}""", false)]
    public void SearchWindowSettings_PreservesLegacyPositionAndExplicitSummonMode(string json, bool followsCursor)
    {
        var settings = JsonSerializer.Deserialize<SearchWindowSettings>(json);

        Assert.IsNotNull(settings);
        Assert.AreEqual(followsCursor, settings.FollowCursorOnSummon);
    }

    [TestMethod]
    public void LocalSendSettingsModel_EnablesHttpsByDefault() =>
        Assert.IsTrue(new LocalSendSettingsModel().EnableHttps);

    [TestMethod]
    public void HotkeyPageSettings_DisablesDoubleClickQuickNavByDefault() =>
        Assert.IsFalse(new HotkeyPageSettings().QuickNavTriggerOnDoubleClick);

    [TestMethod]
    public void HotkeyPageSettings_LeavesQuickNavigationHotkeyUnassignedByDefault() =>
        Assert.IsEmpty(new HotkeyPageSettings().QuickNavigationHotkey);

    [TestMethod]
    public void MainWindowSettings_AllowsMultipleInstancesByDefault() =>
        Assert.IsFalse(new MainWindowSettings().SingleInstance);

    [TestMethod]
    public void DefaultFileManagerSetting_DisablesExplorerTabsByDefault() =>
        Assert.IsFalse(new DefaultFileManagerSetting().OpenFoldersInNewExplorerTabs);
}
