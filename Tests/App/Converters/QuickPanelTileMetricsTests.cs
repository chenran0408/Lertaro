using System.Globalization;
using System.Windows;
using Lertaro.App.Converters;
using Lertaro.Core;

namespace Lertaro.App.Tests.Converters;

[TestClass]
[DoNotParallelize]
public sealed class QuickPanelTileMetricsTests
{
    [TestMethod]
    public void WiderWindowsAddColumnsWithoutStretchingButtons()
    {
        foreach (var size in Enum.GetValues<QuickPanelThumbnailSize>())
            AtSize(size, () =>
            {
                var slot = QuickPanelTileMetrics.SlotFor(380);
                Assert.AreEqual(slot, QuickPanelTileMetrics.SlotFor(2000));
                Assert.IsGreaterThan(QuickPanelTileMetrics.ColumnsFor(380), QuickPanelTileMetrics.ColumnsFor(2000));
                for (var width = 200.0; width < 3000; width += 7.3)
                {
                    var columns = QuickPanelTileMetrics.ColumnsFor(width);
                    Assert.IsLessThan(slot, width - columns * slot);
                    Assert.IsLessThanOrEqualTo(width, columns * slot);
                }
            });
    }

    [TestMethod]
    [DataRow(QuickPanelThumbnailSize.Small, 32.0)]
    [DataRow(QuickPanelThumbnailSize.Medium, 40.0)]
    [DataRow(QuickPanelThumbnailSize.Large, 48.0)]
    [DataRow(QuickPanelThumbnailSize.ExtraLarge, 64.0)]
    public void PictureAndCellGrowTogetherWithCompactNameRoom(QuickPanelThumbnailSize size, double picture) => AtSize(size, () =>
                                                                                                                    {
                                                                                                                        var slot = QuickPanelTileMetrics.SlotFor(800);
                                                                                                                        var converter = new QuickPanelTileMetrics();
                                                                                                                        Assert.AreEqual(picture + 24, slot);
                                                                                                                        Assert.AreEqual(picture, (double)converter.Convert(slot, typeof(double), "Icon", CultureInfo.InvariantCulture));
                                                                                                                        Assert.AreEqual(picture, (double)converter.Convert(slot, typeof(double), "IconHeight", CultureInfo.InvariantCulture));
                                                                                                                        Assert.AreEqual(picture + 30, (double)converter.Convert(slot, typeof(double), "Cell", CultureInfo.InvariantCulture));
                                                                                                                    });

    [TestMethod]
    public void NarrowOrUnmeasuredAreasCannotOverflow()
    {
        var converter = new QuickPanelTileMetrics();
        Assert.AreEqual(20.0, QuickPanelTileMetrics.SlotFor(20));
        Assert.AreEqual(0.0, QuickPanelTileMetrics.IconWidthFor(20));
        Assert.AreEqual(1, QuickPanelTileMetrics.ColumnsFor(20));
        foreach (var width in new[] { double.NaN, double.PositiveInfinity, 0.0 })
            Assert.AreEqual(DependencyProperty.UnsetValue, converter.Convert(width, typeof(double), null!, CultureInfo.InvariantCulture));
    }

    private static void AtSize(QuickPanelThumbnailSize size, Action run)
    {
        var before = QuickPanelTileMetrics.IconSize;
        try { QuickPanelTileMetrics.IconSize = size; run(); }
        finally { QuickPanelTileMetrics.IconSize = before; }
    }
}
