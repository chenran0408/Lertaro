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
    public void BuiltInLightAndDarkAreAvailableWithoutPlugins()
    {
        var themes = ThemeManager.Instance.GetAvailableThemes().ToArray();

        CollectionAssert.AreEqual(new[] { "Light", "Dark" }, themes.Select(theme => theme.Id).ToArray());
        Assert.IsFalse(themes[0].IsDark);
        Assert.IsTrue(themes[1].IsDark);
    }

    [TestMethod]
    public void InstalledPluginChoicesSurviveRestartIncludingBothSystemSlots()
    {
        var themes = new[] { new FakeTheme("AmberLight", false), new FakeTheme("OceanDark", true) };
        var saved = new ThemeSelection("amberlight", "AmberLight", "OceanDark", true);
        var selection = saved.Resolve(themes);
        Assert.AreEqual("AmberLight", selection.ManualId);
        Assert.AreEqual("AmberLight", selection.ActiveId(true));
        Assert.AreEqual("OceanDark", selection.ActiveId(false));
        Assert.AreEqual("AmberLight", (selection with { FollowSystem = false }).ActiveId(false));
    }

    [TestMethod]
    public void MissingThemesAndSlotsWithWrongBrightnessFallBackToBuiltIns()
    {
        var themes = new[] { new FakeTheme("AmberLight", false), new FakeTheme("OceanDark", true) };
        var removed = new ThemeSelection("RetiredDark", "OceanDark", "AmberLight", false);
        Assert.AreEqual(new ThemeSelection("Dark", "Light", "Dark", false), removed.Resolve(themes));
        Assert.AreEqual("Light", (removed with { ManualId = "Missing" }).Resolve(themes).ManualId);
    }
}
