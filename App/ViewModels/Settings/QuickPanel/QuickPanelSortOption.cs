using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.QuickPanel;

public sealed record QuickPanelSortOption(QuickPanelSortMode Value, string Label)
{
    public static IReadOnlyList<QuickPanelSortOption> All => new[]
    {
        new QuickPanelSortOption(QuickPanelSortMode.NameAscending, TranslationManager.Instance["QuickPanel_SortByName"]),
        new QuickPanelSortOption(QuickPanelSortMode.ModifiedDescending, TranslationManager.Instance["QuickPanel_SortByModified"]),
    };
}
