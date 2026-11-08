using Lertaro.App.Views.QuickSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.QuickSearchWindow.Helpers;

[TestClass]
public sealed class QuickSearchWindowPositionerTests
{
    [TestMethod]
    public void CalculatePhysicalPosition_NoSavedPosition_SummonsNearCursor()
    {
        var (physX, physY) = QuickSearchWindowPositioner.CalculatePhysicalPosition(
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 600, targetDpiScaleX: 1.0,
            relativeLeft: null, relativeTop: null,
            cursorPhysX: 960, cursorPhysY: 500);

        // Centred on the cursor horizontally, one step below it vertically.
        Assert.AreEqual(660.0, physX, 0.001);
        Assert.AreEqual(524.0, physY, 0.001);
    }

    [TestMethod]
    public void CalculatePhysicalPosition_MixedDpiCrossMonitor_SummonsNearCursor()
    {
        // Target display: 125% DPI (1.25x), 1080p secondary screen at physical X=3840
        var (physX, physY) = QuickSearchWindowPositioner.CalculatePhysicalPosition(
            waLeft: 3840, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 648, targetDpiScaleX: 1.25,
            relativeLeft: null, relativeTop: null,
            cursorPhysX: 4800, cursorPhysY: 300);

        // Target physical width = 648 * 1.25 = 810px
        // Target physical X = 4800 - 810 / 2 = 4395px
        // Target physical Y = 300 + 24 * 1.25 = 330px
        Assert.AreEqual(4395.0, physX, 0.001);
        Assert.AreEqual(330.0, physY, 0.001);
    }

    [TestMethod]
    public void CalculatePosition_MixedDpi_ReturnsSynchronizedDipCoordinates()
    {
        // Target display: 125% DPI (1.25x), 1080p secondary screen at X=3840
        var (left, top) = QuickSearchWindowPositioner.CalculatePosition(
            waLeft: 3840, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 648, targetDpiScaleX: 1.25, targetDpiScaleY: 1.25,
            relativeLeft: null, relativeTop: null,
            cursorPhysX: 4800, cursorPhysY: 300);

        // Target physical coordinates: (4395, 330)
        // WPF DIP values: Left = 4395 / 1.25 = 3516, Top = 330 / 1.25 = 264
        Assert.AreEqual(3516.0, left, 0.001);
        Assert.AreEqual(264.0, top, 0.001);

        // When WPF multiplies by target DPI scale (1.25), it hits exact target physical pixels
        Assert.AreEqual(4395.0, left * 1.25, 0.001);
        Assert.AreEqual(330.0, top * 1.25, 0.001);
    }

    [TestMethod]
    public void CalculatePhysicalPosition_RelativePercentage_PreservedAcrossMixedDpi()
    {
        var (physX, physY) = QuickSearchWindowPositioner.CalculatePhysicalPosition(
            waLeft: 3840, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 648, targetDpiScaleX: 1.25,
            relativeLeft: 0.2, relativeTop: 0.1,
            cursorPhysX: 4800, cursorPhysY: 300);

        // The remembered spot wins regardless of where the cursor is.
        // Target physical X = 3840 + 0.2 * 1920 = 4224px
        // Target physical Y = 0 + 0.1 * 1080 = 108px
        Assert.AreEqual(4224.0, physX, 0.001);
        Assert.AreEqual(108.0, physY, 0.001);
    }

    [TestMethod]
    public void CalculatePhysicalPosition_CursorAtEdges_ClampedInsideWorkArea()
    {
        // Left edge: the bar would reach past the monitor's left border.
        var (leftClamped, _) = QuickSearchWindowPositioner.CalculatePhysicalPosition(
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 600, targetDpiScaleX: 1.0,
            relativeLeft: null, relativeTop: null,
            cursorPhysX: 10, cursorPhysY: 500);
        Assert.AreEqual(8.0, leftClamped, 0.001);

        // Bottom edge: the bar rides up so the results list keeps room below it
        // (64 bar + 5*48 rows = 304px reserve at 1.0x).
        var (_, bottomClamped) = QuickSearchWindowPositioner.CalculatePhysicalPosition(
            waLeft: 0, waTop: 0, waWidth: 1920, waHeight: 1080,
            windowDipWidth: 600, targetDpiScaleX: 1.0,
            relativeLeft: null, relativeTop: null,
            cursorPhysX: 960, cursorPhysY: 1050);
        Assert.AreEqual(1080.0 - 304.0, bottomClamped, 0.001);
    }
}
