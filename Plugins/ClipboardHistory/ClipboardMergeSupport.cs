namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// Pure merge rule for pasting several selected rows at once: text entries join with line breaks,
/// file lists contribute their paths in selection order (deduped), and image rows carry no text so
/// they cannot contribute. A mixed selection pastes the texts followed by the paths. Null when
/// nothing in the selection can be pasted (for example images only).
/// </summary>
internal static class ClipboardMergeSupport
{
    public static ClipboardEntry? MergeForPaste(IReadOnlyList<ClipboardEntry> entries)
    {
        var texts = new List<string>();
        var paths = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Kind == ClipboardEntryKind.Text && entry.Content.Length > 0)
                texts.Add(entry.Content);
            else if (entry.Kind == ClipboardEntryKind.FilePaths)
                paths.AddRange(entry.PathList);
        }

        var uniquePaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (texts.Count == 0 && uniquePaths.Count == 0) return null;
        if (uniquePaths.Count == 0)
            return Synthetic(ClipboardEntryKind.Text, string.Join("\n", texts));
        if (texts.Count == 0)
            return Synthetic(ClipboardEntryKind.FilePaths, string.Join("\n", uniquePaths));
        return Synthetic(ClipboardEntryKind.Text, string.Join("\n", texts.Concat(uniquePaths)));
    }

    private static ClipboardEntry Synthetic(ClipboardEntryKind kind, string content) =>
        new(0, content, content, string.Empty, string.Empty, false, 1, 0, 0, kind);
}
