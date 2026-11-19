using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
using Lertaro.Core.SearchIndex;
namespace Lertaro.App.ViewModels.Search.Mapping;

public static class SearchableItemMapper
{
    public static void Preload() => SearchableItemCache.Preload();

    // A learned Application history entry retains only its launched path. Reuse the cached provider
    // entry when it is still available so history results keep the application's own title, icon, and
    // execution delegate instead of reconstructing an almost-identical row from the shortcut path.
    internal static bool TryCreateApplicationResult(string path, string query, out AppSearchResult result)
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            SearchableItemCache.EnsureLoaded(provider);
            if (!SearchableItemCache.TryGetEntries(provider.GetType().Name, out var entries))
                continue;

            var entry = entries.FirstOrDefault(entry =>
                string.Equals(entry.Item.ResultKind, "Application", StringComparison.Ordinal) &&
                string.Equals(entry.Item.ActionArgument?.Trim(), path.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                continue;

            result = BuildResult(entry, provider, query);
            return true;
        }

        result = null!;
        return false;
    }

    // Providers load on a background thread and a query issued before a given provider finishes is
    // silently missing its items (see AddSearchableItemResults' cache-miss "continue" below) -- there is
    // no synchronous "wait for everything" alternative without blocking the UI. Instead, a live search
    // re-runs itself once more providers become available, so results stream in rather than staying
    // incomplete for the rest of the session. Raised on a background thread; subscribers must marshal
    // back to the UI thread themselves.
    public static event Action? ProviderLoaded
    {
        add => SearchableItemCache.ProviderLoaded += value;
        remove => SearchableItemCache.ProviderLoaded -= value;
    }

    // Returns candidates (with their ranking MatchRank) instead of appending directly to a results list --
    // the caller (SearchResultMapper.BuildQuickResults) merges these into one globally sorted list
    // alongside favorites/history-matched files/file-search results, rather than always showing every
    // searchable item ahead of every file result regardless of which actually matched better.
    public static List<(AppSearchResult Result, MatchRank Match, bool IsExactAlias)> CollectSearchableItemResults(string query, bool isInlineWindow, string? triggeredTypeId = null)
    {
        var candidates = new List<(AppSearchResult Result, MatchRank Match, bool IsExactAlias)>();
        if (isInlineWindow) return candidates;

        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q)) return candidates;

        // Parsed ONCE for the whole catalog scan. The per-entry string API re-parsed the query for every
        // title and every alias (2+ parses per text, and Parse re-runs each alias provider's
        // GetQueryForms), which dominated this loop's cost on a single keystroke.
        var fuzzy = FuzzyQuery.Parse(q);
        var queryWords = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Every matched entry -- across ALL providers, not just within one -- gets ranked by the same
        // match rank the file search hot path uses (FuzzyMatcher.ComputeBestMatch, against the entry's
        // own title -- same text TextHighlighter shows), instead of a fixed match-kind bucket order capped
        // PER PROVIDER.
        var separators = AliasProviderRegistry.GetActiveProviders().Select(p => p.SyllableSeparator).Where(c => c != '\0').ToArray();
        var aliasQuery = ApplicationSearchAliases.Normalize(q, separators);
        var matched = new List<CatalogMatch>();

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            if (triggeredTypeId != null && SearchResultTypePriority.GetProviderTypeId(provider) != triggeredTypeId)
                continue;
            SearchableItemCache.EnsureLoaded(provider);

            if (!SearchableItemCache.TryGetEntries(provider.GetType().Name, out var entries))
                continue;

            foreach (var entry in entries)
            {
                // The standard match contract (FuzzyMatcher.ComputeBestMatch): title first,
                // then each curated alias, via the same FzfPattern Parse Core's real file search uses
                // -- a multi-word query like "gsh ypfq" correctly requires BOTH words to match
                // somewhere. (Keyword-scoped directory search used to live here as a FileFilter_
                // ResultKind routing over materialized files; it is now a real scoped engine search --
                // see FileFilterScopeResolver.)
                var match = MatchCatalogEntry(fuzzy, queryWords, entry);
                var exactAlias = entry.ExactAliases?.Contains(aliasQuery) == true;
                if (match.IsMatch || exactAlias)
                    matched.Add(new(entry, match, provider, exactAlias));
            }
        }

        foreach (var item in SelectDisplayCandidates(matched, q))
            candidates.Add((BuildResult(item.Entry, item.Provider, q), item.Match, item.ExactAlias));
        return candidates;
    }

    internal readonly record struct CatalogMatch(SearchableItemCache.CacheEntry Entry, MatchRank Match,
        ISearchableItemProvider Provider, bool ExactAlias);

    internal static IEnumerable<CatalogMatch> SelectDisplayCandidates(IEnumerable<CatalogMatch> matched, string query) =>
        matched.OrderByDescending(m => query.Equals(m.Entry.Item.Title, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(m => m.ExactAlias)
            .ThenBy(m => m.Match.Shape).ThenBy(m => m.Match.Start).ThenByDescending(m => m.Match.Weight)
            .Take(50);

    internal static MatchRank MatchCatalogEntry(FuzzyQuery query, string[] queryWords, SearchableItemCache.CacheEntry entry)
    {
        if (IsLiteralShortcut(entry.Item, query.Text))
            return new MatchRank(MatchRank.TierName, 0, 1);
        var match = query.BestMatch(entry.Item.Title, entry.Aliases);
        if (match.IsMatch) return match;
        // Match Windows Control Panel task links: every query token prefixes a title/keyword word.
        // Keyword-only hits have no title highlight evidence, so retain zero weight as a fallback.
        return queryWords.Length > 0 && queryWords.All(token => entry.KeywordWords.Any(
                word => word.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
            ? new MatchRank(MatchRank.TierFull, 0, 0, MatchRank.ShapeNoName)
            : MatchRank.NoMatch;
    }

    private static bool IsLiteralShortcut(SearchableItem item, string query) =>
        query.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && item.ResultKind == "Application"
        && query.Equals(System.IO.Path.GetFileName(item.ActionArgument), StringComparison.OrdinalIgnoreCase);

    // Split out of the matches loop below purely to keep this file's per-method length down -- no
    // other caller.
    private static AppSearchResult BuildResult(SearchableItemCache.CacheEntry entry, ISearchableItemProvider provider, string activeQuery)
    {
        var item = entry.Item;
        System.Windows.Media.ImageSource? iconOverride = null;

        var isRealFile = false;
        var isRealDir = false;
        var isApplication = false;
        var rKind = item.ResultKind ?? string.Empty;

        if (rKind == "File")
        {
            isRealFile = true;
        }
        else if (rKind == "Directory")
        {
            isRealDir = true;
        }
        else if (rKind == "Application")
        {
            // Keep the app's real target path (a Start Menu .lnk, or a virtual shell:AppsFolder
            // token for packaged apps) instead of the generic "__SEARCHABLE_ITEM__:" placeholder,
            // so file actions (copy, locate in explorer, ...) have something to act on -- each
            // action's own CanExecute already handles a path that doesn't exist on disk.
            isApplication = true;
        }

        if (entry.Icon != null)
        {
            // Frozen bitmap materialized once at load time (see EnsureLoaded); reused as-is with
            // no per-keystroke rebuild and no leaked GDI handle.
            iconOverride = entry.Icon;
        }
        else if ((isRealFile || isRealDir || isApplication) && !string.IsNullOrWhiteSpace(item.ActionArgument))
        {
            // Deliberately leave IconOverride unset here -- AppSearchResult.Icon's own getter (FullPath/
            // IsDir are set below to item.ActionArgument/isRealDir, exactly what this needs) already
            // does a cache-only check first, then a background-thread ShellIconHelper extraction
            // (semaphore-limited, marshaled back once ready) the same way every regular file search
            // result already gets its icon. Calling ShellIconHelper.GetIconForPath eagerly here instead
            // would set IconOverride and short-circuit that whole lazy/async path, forcing every File
            // Filter/Application/Settings item through a SYNCHRONOUS extraction on the UI thread -- for
            // a video file on a network drive, that means Windows' shell thumbnail handler reading the
            // file over SMB to decode a frame, blocking the search results render until it returns.
        }
        else if (!string.IsNullOrWhiteSpace(item.IconData))
        {
            try
            {
                var color = string.IsNullOrWhiteSpace(item.IconColor) ? "DefaultPluginIconColor" : item.IconColor;
                iconOverride = ShellIconHelper.CreateVectorIcon(item.IconData, color);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchableItemMapper] Failed to create vector icon: {ex.Message}", LogLevel.Error);
            }
        }
        else
        {
            try
            {
                iconOverride = ShellIconHelper.CreateVectorIcon("M7 2v11h3v9l7-12h-4l3-8z", "DefaultPluginIconColor");
            }
            catch { }
        }

        return new AppSearchResult
        {
            Name = IsLiteralShortcut(item, activeQuery) ? System.IO.Path.GetFileName(item.ActionArgument) : item.Title,
            FullPath = (isRealFile || isRealDir || isApplication) ? item.ActionArgument : $"__SEARCHABLE_ITEM__:{provider.Name}:{item.Title}",
            // Applications show name-only: blank the subtitle so the path row collapses (an app's
            // FullPath is a virtual token anyway). Other item kinds keep their description.
            ParentDir = item.ResultKind == "Application" ? string.Empty : item.Description,
            IsDir = isRealDir,
            Drive = string.Empty,
            ResultKind = isRealFile ? "File" : (isRealDir ? "Directory" : (isApplication ? "Application" : "InstantResult")),
            SearchQuery = activeQuery ?? string.Empty,
            IconOverride = iconOverride,
            InstantResultActionType = item.ActionType ?? "Copy",
            InstantResultActionArgument = item.ActionArgument ?? string.Empty,
            ApplicationArguments = item.LaunchArguments,
            ApplicationWorkingDirectory = item.LaunchWorkingDirectory,
            InstantResultOnExecute = item.OnExecute,
            InstantResultOnExecuteFunc = item.OnExecuteFunc,
            TabCompletion = item.TabCompletion,
            SourceProvider = provider
        };
    }
}
