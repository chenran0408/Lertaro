using Lertaro.App.ViewModels.QuickPanel;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

[TestClass]
public sealed class QuickPanelGridLayoutTests
{
    [TestMethod]
    public void MovingToEmptyCellLeavesOriginalCellEmptyAndOtherIconsUntouched()
    {
        var saved = QuickPanelGridLayout.Merge(new Dictionary<string, int>(), new[] { "a", "b", "c" });
        var moved = QuickPanelGridLayout.Place(saved, new[] { "a" }, 7);
        Assert.AreEqual(7, moved["a"]);
        Assert.AreEqual(1, moved["b"]);
        Assert.AreEqual(2, moved["c"]);
        Assert.IsFalse(moved.Values.Contains(0));
        var reopened = QuickPanelGridLayout.Merge(moved, new[] { "c", "a", "b" });
        CollectionAssert.AreEquivalent(moved.ToList(), reopened.ToList());
    }

    [TestMethod]
    public void MovingToOccupiedCellSwapsOnlyThoseTwoIcons()
    {
        var saved = QuickPanelGridLayout.Merge(new Dictionary<string, int>(), new[] { "a", "b", "c" });
        var moved = QuickPanelGridLayout.Place(saved, new[] { "A" }, 1);
        Assert.AreEqual(1, moved["a"]);
        Assert.AreEqual(0, moved["b"]);
        Assert.AreEqual(2, moved["c"]);
        Assert.HasCount(3, moved.Values.Distinct());
    }

    [TestMethod]
    public void ExternalDropStartsAtRequestedCellAndPreservesExistingIcons()
    {
        var saved = new Dictionary<string, int> { ["existing"] = 5, ["other"] = 6 };
        var moved = QuickPanelGridLayout.Place(saved, new[] { "new-z", "new-a" }, 5);
        Assert.AreEqual(5, moved["new-z"]);
        Assert.AreEqual(7, moved["new-a"]);
        Assert.AreEqual(0, moved["existing"]);
        Assert.AreEqual(6, moved["other"]);
    }

    [TestMethod]
    public void MissingIconsReserveCellsAndMalformedSavedPositionsAreRepaired()
    {
        var saved = new Dictionary<string, int> { ["missing"] = 4, ["bad"] = -1, ["duplicate"] = 4, ["huge"] = int.MaxValue };
        var result = QuickPanelGridLayout.Merge(saved, new[] { "a", "bad", "duplicate", "huge" });
        Assert.AreEqual(4, result["missing"]);
        Assert.HasCount(5, result);
        Assert.HasCount(5, result.Values.Distinct());
        Assert.IsTrue(result.Values.All(slot => slot >= 0 && slot <= QuickPanelGridLayout.MaximumSlot));
    }

    [TestMethod]
    public void GroupReloadAndReopenKeepEmptyCellsAndExactDropPosition()
    {
        var items = new List<(AppSearchResult, DateTime?)> { (new AppSearchResult { Name = "a", FullPath = "a" }, null), (new AppSearchResult { Name = "b", FullPath = "b" }, null) };
        var group = new QuickPanelGroupViewModel("source", "Source", "", items.ToList());
        group.PlaceInGrid(new[] { "a" }, 8);
        group.PlaceInGrid(new[] { "new" }, 3);
        group.Replace(items.Append((new AppSearchResult { Name = "new", FullPath = "new" }, (DateTime?)null)).ToList());
        var reopened = new QuickPanelGroupViewModel("source", "Source", "", items, group.SortMode, itemOrder: group.ItemOrder, gridSlots: group.GridSlots);
        Assert.AreEqual(8, reopened.GridSlots["a"]);
        Assert.AreEqual(3, reopened.GridSlots["new"]);
        Assert.IsTrue(reopened.UsesGridSlots);
        reopened.ApplyFilter("a");
        Assert.IsFalse(reopened.UsesGridSlots);
        reopened.ApplyFilter("");
        Assert.AreEqual(8, reopened.GridSlots["a"]);
    }
}
