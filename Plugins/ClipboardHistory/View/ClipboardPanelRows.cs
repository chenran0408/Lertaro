namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>The filter chips across the panel's top.</summary>
internal enum ClipboardPanelTab { All, Text, Image, Files, Favorites }

/// <summary>One line in the panel list: either a group header (IsHeader) or an entry row.</summary>
internal sealed record ClipboardPanelItem(
    bool IsHeader,
    string HeaderText,
    long Id,
    string Title,
    string Subtitle,
    ClipboardEntryKind Kind,
    string ImagePath,
    bool Pinned,
    string Query = "")
{
    public static ClipboardPanelItem Header(string text) => new(true, text, 0, string.Empty, string.Empty,
        ClipboardEntryKind.Text, string.Empty, false);

    public bool IsRow => !IsHeader;
}

internal sealed record ClipboardPanelGroup(string Label, IReadOnlyList<ClipboardEntry> Entries);

/// <summary>
/// Pure panel list building: tab filter, query match, pinned-first ordering and header insertion.
/// Everything here is plain data in, plain data out -- unit-testable without WPF or a clipboard.
/// </summary>
internal static class ClipboardPanelRows
{
    /// <summary>Orders entries for display: pinned first, then either most-recently-captured
    /// (the default) or most-copied (frequency). Stable on ties via Id, matching the store.</summary>
    public static List<ClipboardEntry> Sort(IReadOnlyList<ClipboardEntry> entries, bool byFrequency)
    {
        if (!byFrequency) return entries.ToList();
        return entries
            .OrderByDescending(entry => entry.Pinned)
            .ThenByDescending(entry => entry.SeenCount)
            .ThenByDescending(entry => entry.LastSeenAtMs)
            .ThenByDescending(entry => entry.Id)
            .ToList();
    }

    public static bool MatchesTab(ClipboardEntry entry, ClipboardPanelTab tab) => tab switch
    {
        ClipboardPanelTab.Text => entry.Kind == ClipboardEntryKind.Text,
        ClipboardPanelTab.Image => entry.Kind == ClipboardEntryKind.Image,
        ClipboardPanelTab.Files => entry.Kind == ClipboardEntryKind.FilePaths,
        ClipboardPanelTab.Favorites => entry.Pinned,
        _ => true,
    };

    /// <summary>Empty query matches everything; otherwise the shared search predicate decides
    /// (preview, content or source process, through the caller's fuzzy matcher).</summary>
    public static bool MatchesQuery(ClipboardEntry entry, string query, Func<string, string, bool> isMatch) =>
        ClipboardSearchSupport.Matches(entry, query, isMatch);

    /// <summary>Filters and orders the visible rows (pinned first, then the store's newest-first),
    /// then groups them: one header for the pinned section, date headers for the rest. Store order
    /// is already pinned DESC then newest DESC, so grouping walks the list once.</summary>
    public static List<ClipboardPanelItem> Build(
        IReadOnlyList<ClipboardEntry> entries,
        ClipboardPanelTab tab,
        string query,
        Func<string, string, bool> isMatch,
        Func<ClipboardEntry, string> subtitle,
        Func<ClipboardEntry, string> dateLabel,
        string pinnedLabel)
    {
        var visible = entries
            .Where(entry => MatchesTab(entry, tab) && MatchesQuery(entry, query, isMatch))
            .ToList();

        var items = new List<ClipboardPanelItem>(visible.Count + 8);
        var currentLabel = string.Empty;
        foreach (var entry in visible)
        {
            var label = entry.Pinned ? pinnedLabel : dateLabel(entry);
            if (!string.Equals(label, currentLabel, StringComparison.Ordinal))
            {
                currentLabel = label;
                items.Add(ClipboardPanelItem.Header(label));
            }
            items.Add(new ClipboardPanelItem(false, string.Empty, entry.Id, entry.PlainPreview,
                subtitle(entry), entry.Kind, entry.ImagePath, entry.Pinned, query));
        }
        return items;
    }

    /// <summary>Ids of the selectable rows (headers excluded), in display order -- the 1~9 quick
    /// paste indexes into this list, and keyboard navigation walks it.</summary>
    public static List<long> SelectableIds(IReadOnlyList<ClipboardPanelItem> items) =>
        items.Where(item => !item.IsHeader).Select(item => item.Id).ToList();
}
