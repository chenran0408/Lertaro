using Lertaro.Plugins.ClipboardHistory.View;

namespace Lertaro.Plugins.ClipboardHistory.Tests.View;

[TestClass]
public sealed class ClipboardPanelPlacementTests
{
    [TestMethod]
    [DataRow(1.0, 780, 620)]
    [DataRow(1.25, 975, 775)]
    [DataRow(1.5, 1170, 930)]
    [DataRow(2.0, 1560, 1240)]
    public void CursorAtUpperLeftRemainsInPhysicalPixels(double scale, int width, int height)
    {
        var placement = ClipboardPanelPlacement.FitToWorkArea(100, 110, scale, 780, 620, (0, 0, 2560, 1440));

        Assert.AreEqual((100, 110, width, height), (placement.Left, placement.Top, placement.Width, placement.Height));
        Assert.AreEqual(780, placement.WidthDip);
        Assert.AreEqual(620, placement.HeightDip);
    }

    [TestMethod]
    public void SwitchingFromPrimaryToScaledMonitorOnTheLeftDoesNotScaleTheDesktopOrigin()
    {
        var primary = ClipboardPanelPlacement.FitToWorkArea(100, 100, 1, 780, 620, (0, 0, 1920, 1040));
        var secondary = ClipboardPanelPlacement.FitToWorkArea(-2280, -240, 1.5, 780, 620, (-2400, -300, 0, 1260));

        Assert.AreEqual((100, 100, 780, 620), (primary.Left, primary.Top, primary.Width, primary.Height));
        Assert.AreEqual((-2280, -240, 1170, 930), (secondary.Left, secondary.Top, secondary.Width, secondary.Height));
    }

    [TestMethod]
    public void ScaledSecondaryMonitorFlipsAtItsBottomRightEdge()
    {
        var placement = ClipboardPanelPlacement.FitToWorkArea(4400, 1310, 1.5, 780, 620, (1920, -100, 4480, 1340));

        Assert.AreEqual((3230, 380, 1170, 930), (placement.Left, placement.Top, placement.Width, placement.Height));
    }

    [TestMethod]
    public void OversizedSavedPanelFitsTheWholeWorkAreaOnAScaledMonitor()
    {
        var placement = ClipboardPanelPlacement.FitToWorkArea(-830, 100, 1.5, 900, 750, (-960, -300, 0, 270));

        Assert.AreEqual((-960, -300, 960, 570), (placement.Left, placement.Top, placement.Width, placement.Height));
        Assert.AreEqual(640, placement.WidthDip);
        Assert.AreEqual(380, placement.HeightDip);
    }
}
