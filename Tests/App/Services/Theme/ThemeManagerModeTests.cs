using System.Windows;
using Lertaro.App.Services.Theme;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Services.Theme;

[TestClass]
public sealed class ThemeManagerModeTests
{
    private sealed class FakeTheme(string id, bool isDark) : ITheme
    {
        public string Id => id;
        public string DisplayName => id;
        public bool IsDark => isDark;
        public ResourceDictionary GetResources() => new();
    }

    [STATestMethod]
    public void AvailableThemesAreOnlythe shell glassLightAndDarkModes()
    {
        var themes = ThemeManager.Instance.GetAvailableThemes().ToArray();

        CollectionAssert.AreEqual(new[] { "Light", "Dark" }, themes.Select(theme => theme.Id).ToArray());
        Assert.IsFalse(themes[0].IsDark);
        Assert.IsTrue(themes[1].IsDark);
    }

    [STATestMethod]
    public void LegacyThemeModeUsesKnownThemeMetadataBeforeItsIdSuffix()
    {
        var legacy = new[] { new FakeTheme("old-preset", isDark: true) };

        Assert.AreEqual("Dark", ThemeManager.ResolveThemeMode("old-preset", "Light", legacy));
        Assert.AreEqual("Light", ThemeManager.ResolveThemeMode("old-preset", "Dark",
            new[] { new FakeTheme("old-preset", isDark: false) }));
    }

    [STATestMethod]
    public void LegacyThemeModeFallsBackToRecognizedSuffixThenRequestedMode()
    {
        Assert.AreEqual("Dark", ThemeManager.ResolveThemeMode("RetiredPresetDark", "Light", []));
        Assert.AreEqual("Light", ThemeManager.ResolveThemeMode("RetiredPresetLight", "Dark", []));
        Assert.AreEqual("Dark", ThemeManager.ResolveThemeMode("UnknownPreset", "Dark", []));
        Assert.AreEqual("Light", ThemeManager.ResolveThemeMode("UnknownPreset", "other", []));
    }
}
