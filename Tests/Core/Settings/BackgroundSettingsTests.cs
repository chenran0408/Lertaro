using System.Text.Json;

namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class BackgroundSettingsTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(999)]
    public void RetiredBackgroundModesFallBackToTheme(int kind)
    {
        var old = JsonSerializer.Deserialize<BackgroundSettings>($"{{\"Kind\":{kind},\"Blur\":20,\"Material\":2,\"GlassLevel\":2}}")!.Normalize();
        Assert.AreEqual(BackgroundKind.Theme, old.Kind);
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
