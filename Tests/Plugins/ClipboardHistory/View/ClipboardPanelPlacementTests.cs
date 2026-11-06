using Lertaro.Plugins.ClipboardHistory.View;

namespace Lertaro.Plugins.ClipboardHistory.Tests.View;

[TestClass]
public sealed class ClipboardPanelPlacementTests
{
    // A 125%-scaled 1920x1080 monitor: the DIP work area is 1536x864 (taskbar excluded for round
    // numbers), the panel defaults to 780x620. This is the shape of the "panel always lands in
    // the bottom-right corner" bug: the pixel cursor used to ride against DIP bounds.
    private const double Scale = 1.25;
    private static readonly (double left, double top, double right, double bottom) Area = (0, 0, 1536, 864);
    private const double Width = 780;
    private const double Height = 620;

    [TestMethod]
    public void CursorInTheUpperLeftRegion_PlacesAtTheCursorInDips()
    {
        // Physical (500,250) = DIP (400,200); room exists down/right, so the panel anchors its
        // top-left exactly at the cursor -- previously the raw pixel value landed it 25% off.
        var (left, top) = ClipboardPanelPlacement.ClampCursor(500, 250, Scale, Width, Height, Area);
        Assert.AreEqual(400, left, 0.01);
        Assert.AreEqual(200, top, 0.01);
    }

    [TestMethod]
    public void OversizedPanelAtPhysicalCenter_ClampsIntoTheWorkArea()
    {
        // The 780x620 panel barely fits a 1536x864 DIP work area: a center cursor has no room on
        // either side, so the clamp wins and the panel sits as far up-left as possible.
        var (left, top) = ClipboardPanelPlacement.ClampCursor(960, 540, Scale, Width, Height, Area);
        Assert.AreEqual(756, left, 0.01);
        Assert.AreEqual(244, top, 0.01);
    }

    [TestMethod]
    public void CursorNearBottomRight_FlipsThePanelUpLeft()
    {
        // Physical (1700,950) = DIP (1360,760): no room down/right for a 780x620 panel, so it
        // opens with its bottom-right corner at the cursor instead of being clamped into the
        // work area's bottom-right corner.
        var (left, top) = ClipboardPanelPlacement.ClampCursor(1700, 950, Scale, Width, Height, Area);
        Assert.AreEqual(1360 - Width, left, 0.01);
        Assert.AreEqual(760 - Height, top, 0.01);
    }

    [TestMethod]
    public void CursorNearTopLeft_AnchorsTopLeftWithoutNegativeOffset()
    {
        var (left, top) = ClipboardPanelPlacement.ClampCursor(50, 40, Scale, Width, Height, Area);
        Assert.AreEqual(40, left, 0.01);   // 50 px / 1.25 = 40 DIP, room down/right exists
        Assert.AreEqual(32, top, 0.01);
    }

    [TestMethod]
    public void PanelWiderThanTheWorkArea_ClampsIntoIt()
    {
        var (left, _) = ClipboardPanelPlacement.ClampCursor(100, 100, Scale, 1600, Height, Area);
        Assert.AreEqual(0, left, 0.01); // max(left, right - width) = 0, the clamp wins
    }

    [TestMethod]
    public void OneHundredPercentScale_PlacesThePanelExactlyAtTheCursor()
    {
        var (left, top) = ClipboardPanelPlacement.ClampCursor(300, 200, 1.0, Width, Height, (0, 0, 1920, 1040));
        Assert.AreEqual(300, left, 0.01);
        Assert.AreEqual(200, top, 0.01);
    }
}
