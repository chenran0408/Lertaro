using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Core;


public class SearchResult : ISearchResult
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>Plugin SDK contract alias for <see cref="Path"/>.</summary>
    public string FullPath => Path;

    /// <summary>Plugin SDK contract: the directory a result lives in (itself for folders).</summary>
    public string ContextDirectory => IsDir ? Path : System.IO.Path.GetDirectoryName(Path) ?? Drive + ":\\";

    public bool IsDir { get; set; }
    public string Drive { get; set; } = string.Empty;

    public bool IsApplication => false;

    public FileAttributes Attributes { get; set; }
    internal ulong RankSortKey { get; set; }

    // Populated from the index for every result that comes from one (local or network/WSL) -- lets
    // SearchService merge GetRecentFiles' local and network/WSL result sets by actual recency instead of
    // just concatenating two already-sorted-but-incomparable lists, lets the sidebar Size/Date filters and
    // the Size column run synchronously off already-known data instead of a per-application IPC round
    // trip (see FileSizeFilterProvider/DateModifiedFilterProvider), and lets the UI show a modified date
    // without re-touching the filesystem (see AppSearchResult.DateModified). Default for results that
    // don't come from an index (plugin results, etc.) or genuinely have no recorded metadata.
    public FileMetadata Metadata { get; set; }
}

public sealed class SearchResultRankComparer : IComparer<SearchResult>
{
    public static readonly SearchResultRankComparer Instance = new(new Dictionary<string, int>());

    private readonly IReadOnlyDictionary<string, int> _historySnapshot;

    private readonly IReadOnlyDictionary<string, int>? _usageCounts;

    public SearchResultRankComparer(IReadOnlyDictionary<string, int> historySnapshot,
        IReadOnlyDictionary<string, int>? usageCounts = null)
    {
        _historySnapshot = historySnapshot;
        _usageCounts = usageCounts;
    }

    private static string NormalizeForLookup(string path)
    {
        if (path.Length > 3 && path[^1] == '\\')
            return path.TrimEnd('\\');
        return path;
    }

    public int Compare(SearchResult? left, SearchResult? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        return CompareRanked(left, GetHistoryRank(left), right, GetHistoryRank(right));
    }

    internal (int Priority, int Opens) GetHistoryRank(SearchResult row)
    {
        var path = NormalizeForLookup(row.Path);
        return (_historySnapshot.GetValueOrDefault(path, int.MaxValue), _usageCounts?.GetValueOrDefault(path) ?? 0);
    }

    internal static int CompareRanked(SearchResult left, (int Priority, int Opens) leftHistory,
        SearchResult right, (int Priority, int Opens) rightHistory)
    {
        var compare = leftHistory.Priority.CompareTo(rightHistory.Priority);
        if (compare != 0)
            return compare;

        compare = left.RankSortKey.CompareTo(right.RankSortKey);
        if (compare != 0)
            return compare;

        // Usage tie-breaker (A2): equal fzf score, the more-opened entry first. A null or all-zero
        // map makes every comparison fall through, so ordering is bit-identical to pre-A2 builds.
        compare = rightHistory.Opens.CompareTo(leftHistory.Opens);
        if (compare != 0)
            return compare;

        compare = left.Path.Length.CompareTo(right.Path.Length);
        if (compare != 0)
            return compare;

        compare = string.Compare(left.Drive, right.Drive, StringComparison.OrdinalIgnoreCase);
        if (compare != 0)
            return compare;

        return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
    }
}
