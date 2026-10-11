using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

[TestClass]
public sealed class QuickPanelItemOrderingTests
{
    [TestMethod]
    public void CustomRanksIgnorePathCaseAndDuplicateSavedEntries()
    {
        var items = new List<(AppSearchResult Item, DateTime? Modified)>
        {
            (new() { Name = "a", FullPath = @"C:\data\a" }, null),
            (new() { Name = "z", FullPath = @"C:\data\z" }, null),
            (new() { Name = "new-b", FullPath = @"C:\data\b" }, null),
            (new() { Name = "new-a", FullPath = @"C:\data\c" }, null)
        };
        var ordered = QuickPanelItemOrdering.Order(items, QuickPanelSortMode.Custom,
            new[] { @"C:\DATA\z", @"C:\data\z", @"C:\data\a" });
        CollectionAssert.AreEqual(new[] { "z", "a", "new-a", "new-b" }, ordered.Select(pair => pair.Item.Name).ToList());
    }
}
