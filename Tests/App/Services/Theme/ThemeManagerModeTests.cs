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
    public void AvailableThemesAreOnlyGlassDeskLightAndDarkModes()
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

        Assert.AreEqual("Dark", ThemeManager.ResolveGlassDeskMode("old-preset", "Light", legacy));
        Assert.AreEqual("Light", ThemeManager.ResolveGlassDeskMode("old-preset", "Dark",
            new[] { new FakeTheme("old-preset", isDark: false) }));
    }

    [STATestMethod]
    public void LegacyThemeModeFallsBackToRecognizedSuffixThenRequestedMode()
    {
        Assert.AreEqual("Dark", ThemeManager.ResolveGlassDeskMode("RetiredPresetDark", "Light", []));
        Assert.AreEqual("Light", ThemeManager.ResolveGlassDeskMode("RetiredPresetLight", "Dark", []));
        Assert.AreEqual("Dark", ThemeManager.ResolveGlassDeskMode("UnknownPreset", "Dark", []));
        Assert.AreEqual("Light", ThemeManager.ResolveGlassDeskMode("UnknownPreset", "other", []));
    }
}
