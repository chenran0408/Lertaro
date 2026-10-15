using Lertaro.App.Services;
using Lertaro.App.ViewModels.SpaceAnalyzer;

namespace Lertaro.App.Tests.ViewModels.SpaceAnalyzer;

[TestClass]
public sealed class SpaceBrowserItemsTests
{
    [TestMethod]
    public void VolumesUseRealCapacityInsteadOfIndexedTotals()
    {
        var result = SpaceBrowserItems.Create([new(@"C:\", "C", 5, true, false)],
            [new(@"C:\", "System", 500, 380, false)]);
        var item = Assert.ContainsSingle(result);
        Assert.AreEqual(120L, item.Size);
        Assert.AreEqual(24.0, item.RelativePercentage, 0.001);
        Assert.IsTrue(item.IsIndexed);
        Assert.IsFalse(item.OpenInFileManager);
        Assert.AreEqual(@"System (C:)", item.Name);
    }

    [TestMethod]
    public void UnindexedUsbAppearsAndCanOpenInFileManager()
    {
        var item = Assert.ContainsSingle(SpaceBrowserItems.Create([], [new(@"E:\", "USB", 32, 24, true)]));
        Assert.IsTrue(item.IsVolume);
        Assert.IsFalse(item.IsIndexed);
        Assert.IsTrue(item.OpenInFileManager);
        Assert.AreEqual(25.0, item.RelativePercentage, 0.001);
    }

    [TestMethod]
    public void DetachedLocalVolumeIsRemovedWhileIndexedUncShareRemains()
    {
        var share = @"\\server\share";
        var items = SpaceBrowserItems.Create([new(@"E:\", "USB", 8, true, false), new(share, "Share", 10, true, false)], []);
        Assert.AreEqual(share, Assert.ContainsSingle(items).Path);
    }

    [TestMethod]
    public void UnreadableCapacityDoesNotRemoveTheDriveOrInventAUsagePercentage()
    {
        var item = Assert.ContainsSingle(SpaceBrowserItems.Create([], [new(@"E:\", "", null, null, true)]));
        Assert.IsTrue(item.OpenInFileManager);
        Assert.AreEqual(0, item.RelativePercentage);
        Assert.IsFalse(item.Volume!.HasCapacity);
    }

    [TestMethod]
    public void FolderBarsStillDescribeTheirShareOfIndexedContent()
    {
        var items = SpaceBrowserItems.Create([new(@"C:\a", "a", 75, true, false), new(@"C:\b", "b", 25, false, false)]);
        Assert.AreEqual(75.0, items[0].RelativePercentage);
        Assert.AreEqual(25.0, items[1].RelativePercentage);
        Assert.IsFalse(items[0].IsVolume);
    }

    [TestMethod]
    [DataRow(100L, 150L, 0L)]
    [DataRow(100L, 25L, 75L)]
    public void CapacityClampsUnexpectedFreeSpace(long total, long free, long used)
        => Assert.AreEqual(used, new SpaceVolumeInfo(@"C:\", "", total, free, false).UsedBytes);

    [TestMethod]
    [DataRow(-1L, 20L)]
    [DataRow(100L, -1L)]
    public void InvalidCapacityIsUnknown(long total, long free)
    {
        var volume = new SpaceVolumeInfo(@"C:\", "", total, free, false);
        Assert.IsFalse(volume.HasCapacity);
        Assert.IsNull(volume.UsedBytes);
    }
}
