using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class BackgroundSettingsTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(999)]
    public void RetiredBackgroundModesFallBackToTheme(int kind)
    {
        var old = JsonSerializer.Deserialize<BackgroundSettings>($"{{\"Kind\":{kind},\"Blur\":20,\"Material\":2,\"GlassLevel\":2}}")!.Normalize();
        Assert.AreEqual(BackgroundKind.Theme, old.Kind);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void LegacyColorPresetsMapToThemeWithoutErasingTheirValues(int kind)
    {
        var old = JsonSerializer.Deserialize<BackgroundSettings>($"{{\"Kind\":{kind},\"FirstColor\":\"#112233\",\"SecondColor\":\"#445566\",\"Angle\":37,\"Tint\":0.4}}")!.Normalize();
        Assert.AreEqual(BackgroundKind.Theme, old.Kind);
        Assert.AreEqual("#112233", old.FirstColor);
        Assert.AreEqual("#445566", old.SecondColor);
        Assert.AreEqual(37, old.Angle);
        Assert.AreEqual(.4, old.Tint);
    }

    [TestMethod]
    public void LegacyPresetSettingsSurviveLoadAndSave()
    {
        const string json = "{\"Kind\":4,\"FirstColor\":\"#112233\",\"SecondColor\":\"#445566\",\"Angle\":37,\"Blur\":20,\"Tint\":0.4,\"Material\":2,\"GlassLevel\":2}";
        var loaded = JsonSerializer.Deserialize<BackgroundSettings>(json)!.Normalize();
        var saved = JsonSerializer.Deserialize<BackgroundSettings>(JsonSerializer.Serialize(loaded))!.Normalize();

        Assert.AreEqual(BackgroundKind.Material, saved.Kind);
        Assert.AreEqual("#112233", saved.FirstColor);
        Assert.AreEqual("#445566", saved.SecondColor);
        Assert.AreEqual(37, saved.Angle);
        Assert.AreEqual(20, saved.Blur);
        Assert.AreEqual(.4, saved.Tint);
        Assert.AreEqual(WindowMaterial.LiquidGlass, saved.Material);
        Assert.AreEqual(GlassLevel.StrongRefraction, saved.GlassLevel);
    }

    [TestMethod]
    public void ImageOpacityAndFitSurviveSaveAndOldImagePreferencesStillLoad()
    {
        var expected = new BackgroundSettings { Kind = BackgroundKind.Image, ImagePath = @"D:\Pictures\背景.png", Opacity = .75, FitImage = true };
        Assert.AreEqual(expected, JsonSerializer.Deserialize<BackgroundSettings>(JsonSerializer.Serialize(expected))!.Normalize());
        var old = JsonSerializer.Deserialize<BackgroundSettings>("{\"Kind\":3,\"ImagePath\":\"image.png\",\"Blur\":12}")!.Normalize();
        Assert.AreEqual(BackgroundKind.Image, old.Kind);
        Assert.AreEqual("image.png", old.ImagePath);
        Assert.AreEqual(.3, old.Opacity);
    }

    [TestMethod]
    [DataRow(-1d, 0d)]
    [DataRow(2d, 1d)]
    [DataRow(double.NaN, .3)]
    [DataRow(double.PositiveInfinity, .3)]
    public void OpacityIsBounded(double value, double expected)
        => Assert.AreEqual(expected, new BackgroundSettings { Opacity = value }.Normalize().Opacity);
}
