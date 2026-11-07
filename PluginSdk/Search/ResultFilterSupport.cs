using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.PluginSdk.Search;

/// <summary>Named date buckets for the <c>datemodified:</c> filter and the time chips.</summary>
public enum ResultDateRange
{
    None,
    /// <summary>Modified on the current local calendar day (00:00 local, not a rolling 24h).</summary>
    Today,
    /// <summary>Modified since the start of the current local calendar week (Monday 00:00).</summary>
    ThisWeek,
    /// <summary>Modified inside the given local year-month prefix (yyyy-MM).</summary>
    MonthPrefix,
}

/// <summary>
/// One parsed set of result filters -- the shared backend behind both query filter keywords
/// (<c>folder:</c> <c>file:</c> <c>ext:</c> <c>datemodified:</c>) and the results chips. Pure data
/// plus one row predicate; no IO, no query-text knowledge.
/// </summary>
public sealed record ResultFilters
{
    public bool FilesOnly { get; init; }
    public bool FoldersOnly { get; init; }
    public IReadOnlySet<string>? Extensions { get; init; }
    public ResultDateRange DateRange { get; init; } = ResultDateRange.None;
    /// <summary>Local year-month prefix ("2026-11") for MonthPrefix ranges.</summary>
    public string? MonthPrefix { get; init; }
    /// <summary>Case-insensitive full-path prefix ("d:\docs").</summary>
    public string? FolderPrefix { get; init; }

    public bool IsEmpty =>
        !FilesOnly && !FoldersOnly
        && (Extensions is null || Extensions.Count == 0)
        && DateRange == ResultDateRange.None
        && string.IsNullOrEmpty(FolderPrefix);

    /// <summary>Per-row check, suitable for streaming filters. A row that fails any active
    /// dimension is out; dimensions compose with AND.</summary>
    public bool Matches(ISearchResult row)
    {
        if (FoldersOnly && !row.IsDir) return false;
        if (FilesOnly && row.IsDir) return false;

        if (Extensions is { Count: > 0 })
        {
            if (row.IsDir) return false;
            var extension = System.IO.Path.GetExtension(row.FullPath).TrimStart('.').ToLowerInvariant();
            if (!Extensions.Contains(extension)) return false;
        }

        if (FolderPrefix is { Length: > 0 })
        {
            if (!row.FullPath.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (DateRange != ResultDateRange.None)
        {
            var modified = row.Metadata.Modified;
            var today = DateTime.Today;
            switch (DateRange)
            {
                case ResultDateRange.Today:
                    if (modified < today) return false;
                    break;
                case ResultDateRange.ThisWeek:
                    // Calendar week starting Monday: walk back from today to the most recent Monday.
                    var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                    if (modified < weekStart) return false;
                    break;
                case ResultDateRange.MonthPrefix:
                    var monthPrefix = today.ToString("yyyy-MM");
                    if (MonthPrefix is { Length: > 0 } prefix) monthPrefix = prefix;
                    if (modified.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)
                            .StartsWith(monthPrefix, StringComparison.Ordinal))
                        break;
                    return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Shared result-filter backend: the chip kinds map onto the same predicates the query keywords
/// produce, so both entry points filter identically. Extension sets here are the chip presets.
/// </summary>
public static class ResultFilterSupport
{
    public const string ImageExtensions = "png,jpg,jpeg,gif,webp,bmp,svg";
    public const string DocumentExtensions = "pdf,doc,docx,xls,xlsx,ppt,pptx,txt,md,csv";

    /// <summary>Well-known chip kinds: all / folder / file / images / documents / thisweek.</summary>
    public static ResultFilters ForChip(string? chip) => chip switch
    {
        "folder" => new ResultFilters { FoldersOnly = true },
        "file" => new ResultFilters { FilesOnly = true },
        "images" => new ResultFilters { Extensions = ExtensionSet(ImageExtensions) },
        "documents" => new ResultFilters { Extensions = ExtensionSet(DocumentExtensions) },
        "thisweek" => new ResultFilters { DateRange = ResultDateRange.ThisWeek },
        _ => new ResultFilters(),
    };

    public static IReadOnlySet<string> ExtensionSet(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();
}
