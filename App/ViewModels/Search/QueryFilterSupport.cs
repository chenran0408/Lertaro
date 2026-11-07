using System.Text.RegularExpressions;
using Lertaro.PluginSdk.Search;

namespace Lertaro.App.ViewModels.Search;

/// <summary>
/// Parses the A1 query filter keywords (<c>folder:</c> <c>file:</c> <c>ext:</c>
/// <c>datemodified:</c>) out of a raw query, anywhere in the text, and exposes the parsed set as an
/// ambient per-search flow so the streaming renderer can drop non-matching rows as they arrive --
/// per-row, inside the enumeration, never collect-then-filter. The active set lives in an
/// AsyncLocal exactly like <c>SearchContext</c>'s switches: set when a dispatch begins, it flows
/// down that search's whole async chain (streaming renders included) and dies with it.
/// </summary>
public static class QueryFilterSupport
{
    private static readonly Regex TokenRegex = new(
        @"(?:^|\s)(?<key>folder|file|ext|datemodified):(?:""(?<qvalue>[^""]*)""|(?<bvalue>\S*))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly AsyncLocal<ResultFilters?> ActiveLocal = new();

    /// <summary>The filters for the search currently being dispatched. Null = unfiltered.</summary>
    public static ResultFilters? ActiveFilters => ActiveLocal.Value;

    private static readonly AsyncLocal<bool> StealthLocal = new();

    /// <summary>True while a ";" stealth query is being dispatched -- history stores check this
    /// to suppress recording for the current async chain only.</summary>
    public static bool IsStealth => StealthLocal.Value;

    public static void SetStealth(bool stealth) => StealthLocal.Value = stealth;

    /// <summary>Extracts every filter keyword occurrence and returns the query without them. The
    /// returned filters object is shared; callers push it through <see cref="Activate"/>.</summary>
    public static string Extract(string query, out ResultFilters filters)
    {
        var filesOnly = false;
        var foldersOnly = false;
        IReadOnlySet<string>? extensions = null;
        var dateRange = ResultDateRange.None;
        var monthPrefix = default(string);
        var folderPrefix = default(string);

        var cleaned = TokenRegex.Replace(query, match =>
        {
            var value = match.Groups["qvalue"].Success
                ? match.Groups["qvalue"].Value
                : match.Groups["bvalue"].Value;
            switch (match.Groups["key"].Value.ToLowerInvariant())
            {
                case "folder" when value.Length > 0:
                    folderPrefix = value.TrimEnd('\\');
                    return string.Empty;
                case "file":
                    filesOnly = true;
                    return string.Empty;
                case "folder":
                    foldersOnly = true;
                    return string.Empty;
                case "ext" when value.Length > 0:
                    extensions = ResultFilterSupport.ExtensionSet(value);
                    return string.Empty;
                case "datemodified" when value.Length > 0:
                    (dateRange, monthPrefix) = ParseDateValue(value);
                    return string.Empty;
                default:
                    return match.Value; // bare "folder:"/"ext:" with no value: leave the text alone
            }
        });

        filters = new ResultFilters
        {
            FilesOnly = filesOnly,
            FoldersOnly = foldersOnly,
            Extensions = extensions,
            DateRange = dateRange,
            MonthPrefix = monthPrefix,
            FolderPrefix = folderPrefix,
        };
        return Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
    }

    /// <summary>Installs the set for the dispatch that is about to run (and its streaming renders).</summary>
    public static void Activate(ResultFilters filters) => ActiveLocal.Value = filters.IsEmpty ? null : filters;

    /// <summary>Clears at dispatch end so late callbacks of a finished search cannot leak filters
    /// into a later one that parsed none.</summary>
    public static void Clear() => ActiveLocal.Value = null;

    private static (ResultDateRange, string?) ParseDateValue(string value)
    {
        if (value.Equals("today", StringComparison.OrdinalIgnoreCase)) return (ResultDateRange.Today, null);
        if (value.Equals("thisweek", StringComparison.OrdinalIgnoreCase)) return (ResultDateRange.ThisWeek, null);
        if (value.Length == 7 && value[4] == '-' &&
            int.TryParse(value.AsSpan(0, 4), out var year) &&
            int.TryParse(value.AsSpan(5, 2), out var month) &&
            month is >= 1 and <= 12)
            return (ResultDateRange.MonthPrefix, $"{year:D4}-{month:D2}");
        return (ResultDateRange.None, null); // unknown value: leave the text alone, filter nothing
    }
}
