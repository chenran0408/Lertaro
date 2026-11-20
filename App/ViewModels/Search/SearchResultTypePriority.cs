using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.PluginManagerCore;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Search;

// Resolves the "type" tier a BuildQuickResults candidate belongs to for the quick window's
// user-orderable result-type-priority feature (UserSettings.ResultTypeOrder) -- generalizes what used
// to be a single "boost applications" toggle into an id per ISearchableItemProvider (Applications,
// Settings, File Filters, any third-party plugin) plus one synthetic id for raw file-index results.
public static class SearchResultTypePriority
{
    // No ISearchableItemProvider sits behind the fileResults candidate loop in BuildQuickResults --
    // mirrors the "__builtin::..." synthetic-id convention QuickPanelSettings.TabOrder already uses
    // for its own non-plugin entries.
    public const string FilesTypeId = "__builtin::Files";

    public const string FoldersTypeId = "__builtin::Folders";

    public static string GetProviderTypeId(ISearchableItemProvider provider) =>
        PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(provider), PluginComponentType.SearchableItemProvider, provider.GetType().Name);

    // Position in the user's saved order (most-preferred first); an id that isn't listed yet falls back
    // to int.MaxValue, which -- since the caller's sort is stable -- lands it after every listed type
    // while preserving its original relative order against any OTHER unlisted type, matching the same
    // fallback convention QuickNavigationProviderOrder/QuickPanel.TabOrder already use.
    public static int Rank(string typeId, List<string> order)
    {
        var idx = order.IndexOf(typeId);
        return idx >= 0 ? idx : int.MaxValue;
    }

    // Preserve a saved order from before folders had their own position.
    public static List<string> WithFolders(List<string> order)
    {
        if (order.Contains(FoldersTypeId))
            return order;
        var files = order.IndexOf(FilesTypeId);
        if (files < 0)
            return order;
        var withFolders = new List<string>(order);
        withFolders.Insert(files + 1, FoldersTypeId);
        return withFolders;
    }

    public static SearchCategoryFilter IndexCategoryFor(string? id) => id switch
    {
        FilesTypeId => SearchCategoryFilter.File,
        FoldersTypeId => SearchCategoryFilter.Folder,
        _ => SearchCategoryFilter.All
    };

    public static string? ResolveTrigger(char firstChar, IReadOnlyDictionary<string, string> triggers)
    {
        foreach (var (typeId, trigger) in triggers)
        {
            if (trigger is { Length: 1 } && char.ToUpperInvariant(trigger[0]) == char.ToUpperInvariant(firstChar))
                return typeId;
        }
        return null;
    }

    // Same display text ResultTypeOrderViewModel shows for this id in Settings -- used by
    // SearchDispatchController to name the type in its "keep typing to search only X" prompt when a
    // trigger was typed with nothing after it yet.
    public static string? GetDisplayName(string typeId)
    {
        if (typeId == FilesTypeId)
            return TranslationManager.Instance["General_ResultTypeFiles"];
        if (typeId == FoldersTypeId)
            return TranslationManager.Instance["General_ResultTypeFolders"];

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            if (GetProviderTypeId(provider) == typeId)
                return provider.Name;
        }
        return null;
    }

    // Used whenever the quick window's current query is carried over somewhere that has no concept
    // of a per-type trigger at all -- opening the full/main SearchWindow (Ctrl+F, or clicking "Show
    // more search results"). Without this, a query like ";vs" would land in the full window searching
    // literally for ";vs" instead of "vs".
    public static string StripLeadingTrigger(string query)
    {
        if (query.Length == 0)
            return query;
        return StripLeadingTrigger(query, UserSettings.Load().ResultTypeTriggers);
    }

    // Same rule against a caller-supplied trigger table, for the paths that already have the settings
    // graph loaded or need the decision without touching it (see SearchResultExecutionHelper).
    public static string StripLeadingTrigger(string query, IReadOnlyDictionary<string, string> triggers)
    {
        var search = QueryFilterSupport.StripStealthPrefix(query);
        if (search.Length == 0 || ResolveTrigger(search[0], triggers) == null)
            return query;
        return QueryFilterSupport.IsStealthQuery(query) ? ";" + search[1..] : search[1..];
    }
}
