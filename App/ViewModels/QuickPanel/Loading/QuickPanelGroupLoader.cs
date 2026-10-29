using Lertaro.App.ViewModels.Search;
using Lertaro.App.Helpers;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.QuickPanel.Loading;

// Split from QuickPanelViewModelLoading to keep the view-model files under the repository line limit.
// It owns only one source's progressive load; the view model retains tab placement and visible state.
internal sealed class QuickPanelGroupLoader
{
    private readonly Func<QuickPanelFolderSource, IProgress<IReadOnlyList<SearchResult>>, CancellationToken, Task<List<SearchResult>>> _load;
    private readonly bool _mapOnBackground;
    private readonly Func<ExclusionRuleSet> _exclusionRules;

    public QuickPanelGroupLoader(
        Func<QuickPanelFolderSource, IProgress<IReadOnlyList<SearchResult>>, CancellationToken, Task<List<SearchResult>>> load,
        bool mapOnBackground,
        Func<ExclusionRuleSet>? exclusionRules = null)
    {
        _load = load;
        _mapOnBackground = mapOnBackground;
        _exclusionRules = exclusionRules ?? (() => ExclusionRuleSet.From(UserSettings.Load()));
    }

    public async Task LoadAsync(
        QuickPanelTab workspace,
        QuickPanelFolderSource source,
        Action<QuickPanelGroupViewModel> place,
        CancellationToken token)
    {
        workspace.GroupPreferences.TryGetValue(source.Id, out var preference);
        var exclusionRules = _exclusionRules();
        QuickPanelGroupViewModel? group = null;
        var acceptingProgress = true;
        var progress = new Progress<IReadOnlyList<SearchResult>>(batch =>
        {
            if (!acceptingProgress || token.IsCancellationRequested)
                return;

            var items = Map(batch, exclusionRules);
            if (items.Count == 0)
                return;

            if (group == null)
            {
                group = Create(source, preference, items, isLoading: true);
                place(group);
            }
            else
            {
                group.AppendLoading(items);
            }
        });

        List<SearchResult> results;
        try
        {
            results = await _load(source, progress, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickPanel] Source '{source.Path}' failed to load: {ex.Message}", LogLevel.Error);
            return;
        }

        acceptingProgress = false;
        token.ThrowIfCancellationRequested();
        var completeItems = _mapOnBackground
            ? await Task.Run(() => Map(results, exclusionRules), token).ConfigureAwait(true)
            : Map(results, exclusionRules);
        if (group == null)
        {
            if (completeItems.Count == 0 && (!source.AcceptsDrops || !System.IO.Directory.Exists(FavoritePathResolver.Resolve(source.Path))))
                return;

            group = Create(source, preference, completeItems, isLoading: false);
            place(group);
            return;
        }

        group.Replace(completeItems);
    }

    internal static List<(AppSearchResult Item, DateTime? Modified)> Map(IEnumerable<SearchResult> results, ExclusionRuleSet? exclusionRules = null) => results
        .Where(result => !System.IO.Path.IsPathFullyQualified(result.Path)
            || !(exclusionRules ?? ExclusionRuleSet.Empty).IsExcludedPath(result.Path, result.IsDir))
        .Select((result, index) => (
            Item: SearchResultHelper.CreateUiResult(result, string.Empty, index, isApplication: false, scope: null),
            Modified: ReadModified(result)))
        .ToList();

    private static QuickPanelGroupViewModel Create(
        QuickPanelFolderSource source,
        QuickPanelGroupPreference? preference,
        List<(AppSearchResult Item, DateTime? Modified)> items,
        bool isLoading) => new(
            source.Id,
            TitleOf(source, preference),
            FavoritePathResolver.Resolve(source.Path),
            items,
            preference?.Sort ?? QuickPanelGroupPreference.DefaultSortFor(source),
            preference?.ThumbnailView ?? true,
            preference?.Expanded ?? true,
            source.AcceptsDrops,
            isLoading: isLoading,
            maxItems: source.MaxItems,
            itemOrder: preference?.ItemOrder, gridSlots: preference?.GridSlots, itemLabels: preference?.ItemLabels);

    private static string TitleOf(QuickPanelFolderSource source, QuickPanelGroupPreference? preference)
        => string.IsNullOrWhiteSpace(preference?.DisplayName)
            ? FavoritePathResolver.GetDisplayName(source.Path)
            : preference!.DisplayName.Trim();

    private static DateTime? ReadModified(SearchResult item)
        => item.Metadata.Modified is var modified && modified != DateTime.MinValue ? modified : null;
}
