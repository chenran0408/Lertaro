using Lertaro.App.Views.Controls;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass]
public sealed class BackgroundSurfaceTests
{
    [StaTestMethod]
    [DataRow(0d)]
    [DataRow(90d)]
    [DataRow(135d)]
    [DataRow(180d)]
    [DataRow(270d)]
    [DataRow(360d)]
    public void GradientDirectionKeepsBothColorsInsideThePreview(double angle)
    {
        var brush = BackgroundSurface.CreateGradient(new BackgroundSettings { Angle = angle, FirstColor = "#0000FF", SecondColor = "#FF0000" });
        foreach (var coordinate in new[] { brush.StartPoint.X, brush.StartPoint.Y, brush.EndPoint.X, brush.EndPoint.Y })
        {
            Assert.IsGreaterThanOrEqualTo(0d, coordinate);
            Assert.IsLessThanOrEqualTo(1d, coordinate);
        }
        Assert.AreNotEqual(brush.StartPoint, brush.EndPoint);
        Assert.IsTrue(brush.IsFrozen);
    }
}
