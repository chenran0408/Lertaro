using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

[TestClass]
public sealed class QuickPanelGroupViewModelTests
{
    [TestMethod]
    public void DraggingToTheNextSlotMovesForwardInsteadOfDoingNothing()
    {
        var group = Group(3);
        group.MoveTo(@"C:\source\0000", @"C:\source\0001");
        CollectionAssert.AreEqual(new[] { "0001", "0000", "0002" }, group.Items.Select(item => item.Name).ToList());
        group.MoveTo(@"C:\source\0002", @"C:\source\0001");
        CollectionAssert.AreEqual(new[] { "0002", "0001", "0000" }, group.Items.Select(item => item.Name).ToList());
        group.MoveTo(@"C:\source\0002", @"C:\source\0000");
        CollectionAssert.AreEqual(new[] { "0001", "0000", "0002" }, group.Items.Select(item => item.Name).ToList());
        Assert.AreEqual(QuickPanelSortMode.Custom, group.SortMode);
        group.MoveTo(@"C:\source\0001", null);
        Assert.AreEqual("0001", group.Items.Last().Name);
    }

    [TestMethod]
    public void CustomOrderKeepsSavedPathsAndAppendsNewItemsByName()
    {
        var group = new QuickPanelGroupViewModel("source", "Source", @"C:\source", Pairs("z", "new-z", "a", "new-a"),
            QuickPanelSortMode.Custom, itemOrder: new[] { @"C:\SOURCE\z", @"C:\source\missing", @"C:\source\a", @"C:\source\z" });
        CollectionAssert.AreEqual(new[] { "z", "a", "new-a", "new-z" }, group.Items.Select(item => item.Name).ToList());
        group.MoveBefore(@"C:\source\a", @"C:\source\z");
        CollectionAssert.AreEqual(new[] { "a", "z", "new-a", "new-z" }, group.Items.Select(item => item.Name).ToList());
        Assert.IsTrue(group.ItemOrder.Contains(@"C:\source\missing"));
        group.SortMode = QuickPanelSortMode.NameAscending;
        group.SortMode = QuickPanelSortMode.Custom;
        Assert.AreEqual("a", group.Items[0].Name);
        group.Replace(Pairs("new-z", "a", "z", "new-a"));
        Assert.AreEqual("a", group.Items[0].Name);
    }

    [TestMethod]
    public void MovingToEndAndInvalidTargetsPreserveEveryItem()
    {
        var group = Group(80);
        group.MoveBefore(@"C:\source\0000", null);
        while (group.LoadNextPage()) { }
        Assert.AreEqual("0000", group.Items.Last().Name);
        Assert.HasCount(80, group.Items);
        var before = group.ItemOrder.ToList();
        group.MoveBefore(@"C:\source\0001", @"C:\source\unknown");
        group.MoveBefore(@"C:\source\0001", @"C:\source\0001");
        group.MoveBefore(@"C:\source\unknown", null);
        CollectionAssert.AreEqual(before, group.ItemOrder.ToList());
    }

    [TestMethod]
    public void SearchingAndLoadingPreventRearranging()
    {
        var group = Group(3);
        group.IsReordering = true;
        group.ApplyFilter("0001");
        Assert.IsFalse(group.IsReordering);
        Assert.IsFalse(group.CanArrange);
        var before = group.GridSlots.ToList();
        group.PlaceInGrid(new[] { @"C:\source\0001" }, 9);
        CollectionAssert.AreEqual(before, group.GridSlots.ToList());
        Assert.AreEqual(QuickPanelSortMode.NameAscending, group.SortMode);
        group.ApplyFilter("");
        Assert.IsTrue(group.CanArrange);
        var loading = new QuickPanelGroupViewModel("source", "Source", "", Pairs("a", "b"), isLoading: true);
        loading.IsReordering = true;
        Assert.IsFalse(loading.IsReordering);
        loading.Replace(Pairs("a", "b"));
        Assert.IsTrue(loading.CanArrange);
    }

    [TestMethod]
    public void LargeGroup_MaterializesOnlyTheFirstPageUntilRequested()
    {
        var group = Group(300);

        Assert.AreEqual(300, group.Count);
        Assert.HasCount(32, group.Items);

        var loadedPages = 0;
        while (group.LoadNextPage())
            loadedPages++;

        Assert.AreEqual(9, loadedPages);
        Assert.HasCount(300, group.Items);
        Assert.IsFalse(group.LoadNextPage());
    }

    [TestMethod]
    public void ResetMaterialization_PreviouslyScrolledGroup_ReturnsToFirstPage()
    {
        var group = Group(300);
        group.LoadNextPage();
        group.LoadNextPage();
        Assert.HasCount(96, group.Items);

        group.ResetMaterialization();

        Assert.AreEqual(300, group.Count);
        Assert.HasCount(32, group.Items);
    }

    [TestMethod]
    public void ConfiguredMaximum_StillCapsLoadingAndMaterialization()
    {
        var group = new QuickPanelGroupViewModel("source", "Source", @"C:\source", Pairs(Enumerable.Range(0, 300).Select(index => index.ToString()).ToArray()),
            QuickPanelSortMode.NameAscending, maxItems: 20);

        Assert.AreEqual(20, group.Count);
        Assert.HasCount(20, group.Items);
        Assert.IsFalse(group.LoadNextPage());
    }

    [TestMethod]
    public void LoadingGroup_KeepsArrivalOrderUntilTheCompletedSortArrives()
    {
        var group = new QuickPanelGroupViewModel("source", "Source", @"C:\source", Pairs("z", "a"),
            QuickPanelSortMode.NameAscending, isLoading: true);

        CollectionAssert.AreEqual(new[] { "z", "a" }, group.Items.Select(item => item.Name).ToList());

        group.Replace(Pairs("z", "a"));

        CollectionAssert.AreEqual(new[] { "a", "z" }, group.Items.Select(item => item.Name).ToList());
    }

    [TestMethod]
    public void CancelingADragPreservesAutomaticSortAndStoredGrid()
    {
        var group = Group(3);
        group.PlaceInGrid(new[] { @"C:\source\0000" }, 8);
        group.SortMode = QuickPanelSortMode.NameAscending;
        var saved = group.GridSlots.ToList();
        group.IsReordering = true;
        Assert.AreEqual(QuickPanelSortMode.NameAscending, group.SortMode);
        group.IsReordering = false;
        CollectionAssert.AreEqual(saved, group.GridSlots.ToList());
        CollectionAssert.AreEqual(new[] { "0000", "0001", "0002" }, group.Items.Select(item => item.Name).ToList());
    }

    [TestMethod]
    public void DroppingFromAutomaticSortUsesVisiblePositionsInsteadOfAnOlderGrid()
    {
        var group = Group(3);
        group.PlaceInGrid(new[] { @"C:\source\0000" }, 8);
        group.SortMode = QuickPanelSortMode.NameAscending;
        group.PlaceInGrid(new[] { @"C:\source\0000" }, 1);
        Assert.AreEqual(QuickPanelSortMode.Custom, group.SortMode);
        Assert.AreEqual(1, group.GridSlots[@"C:\source\0000"]);
        Assert.AreEqual(0, group.GridSlots[@"C:\source\0001"]);
        Assert.AreEqual(2, group.GridSlots[@"C:\source\0002"]);
    }

    [TestMethod]
    public void RearrangingListRowsIsAlsoRememberedWhenReturningToIcons()
    {
        var group = Group(3);
        group.PlaceInGrid(new[] { @"C:\source\0000" }, 8);
        group.IsThumbnailView = false;
        group.MoveTo(@"C:\source\0000", @"C:\source\0001");
        group.IsThumbnailView = true;
        Assert.AreEqual(0, group.GridSlots[@"C:\source\0000"]);
        Assert.AreEqual(1, group.GridSlots[@"C:\source\0001"]);
        Assert.AreEqual(2, group.GridSlots[@"C:\source\0002"]);
    }

    private static QuickPanelGroupViewModel Group(int count) => new("source", "Source", @"C:\source",
        Pairs(Enumerable.Range(0, count).Select(index => index.ToString("D4")).ToArray()), QuickPanelSortMode.NameAscending);

    private static List<(AppSearchResult Item, DateTime? Modified)> Pairs(params string[] names) => names
        .Select(name => (Item: new AppSearchResult { Name = name, FullPath = @"C:\source\" + name }, Modified: (DateTime?)null))
        .ToList();
}
