using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class BackgroundSettingsTests
{
    [TestMethod]
    public void OldSettingsKeepThemeBackground()
        => Assert.AreEqual(BackgroundKind.Theme, JsonSerializer.Deserialize<UserSettings>("{}")!.Background.Kind);

    [TestMethod]
    public void BackgroundRoundTripPreservesChoices()
    {
        var original = new BackgroundSettings { Kind = BackgroundKind.Image, ImagePath = @"D:\Pictures\背景.png", Blur = 12, Tint = .4, FitImage = true };
        Assert.AreEqual(original, JsonSerializer.Deserialize<BackgroundSettings>(JsonSerializer.Serialize(original)));
    }

    [TestMethod]
    public void NormalizeRejectsInvalidColorsAndLimitsImportedValues()
    {
        var actual = new BackgroundSettings { Kind = (BackgroundKind)999, Material = (WindowMaterial)999, FirstColor = "red", SecondColor = "#ZZZZZZ", Angle = -5, Blur = 400, Tint = double.NaN }.Normalize();
        Assert.AreEqual(BackgroundKind.Theme, actual.Kind);
        Assert.AreEqual(WindowMaterial.Acrylic, actual.Material);
        Assert.AreEqual("#DDEBFA", actual.FirstColor);
        Assert.AreEqual("#EADFF7", actual.SecondColor);
        Assert.AreEqual(0d, actual.Angle);
        Assert.AreEqual(30d, actual.Blur);
        Assert.AreEqual(.65, actual.Tint);
    }

    [TestMethod]
    [DataRow("#abcdef", true)]
    [DataRow("#ABCDEF", true)]
    [DataRow("#123456", true)]
    [DataRow("#fff", false)]
    [DataRow("#00000000", false)]
    [DataRow("#00000z", false)]
    [DataRow(null, false)]
    public void ValidateColor(string? value, bool expected)
        => Assert.AreEqual(expected, BackgroundSettings.ValidColor(value));
}
