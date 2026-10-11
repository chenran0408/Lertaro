using Lertaro.Core;

namespace Lertaro.App.ViewModels.QuickPanel;

// Keeps item ordering separate from the group's rendering and paging logic.
internal static class QuickPanelItemOrdering
{
    internal static IEnumerable<(AppSearchResult Item, DateTime? Modified)> Order(
        IEnumerable<(AppSearchResult Item, DateTime? Modified)> items,
        QuickPanelSortMode mode,
        IReadOnlyList<string> order)
    {
        if (mode == QuickPanelSortMode.NameAscending)
            return items.OrderBy(pair => pair.Item.Name, StringComparer.CurrentCultureIgnoreCase);
        if (mode != QuickPanelSortMode.Custom)
            return items.OrderByDescending(pair => pair.Modified ?? DateTime.MinValue);

        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < order.Count; index++)
            ranks.TryAdd(order[index], index);
        // New files follow the saved items in name order without disturbing the arrangement.
        return items.OrderBy(pair => ranks.GetValueOrDefault(pair.Item.FullPath, int.MaxValue))
            .ThenBy(pair => pair.Item.Name, StringComparer.CurrentCultureIgnoreCase);
    }
}
