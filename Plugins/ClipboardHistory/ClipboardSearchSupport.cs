namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// The one search predicate every query path shares: a term matches an entry when it fuzzy-matches
/// the list preview, the stored content (for file lists that is the normalized paths), the source
/// process name, or the user's note. The caller supplies the matcher -- the runtime and the panel
/// both pass the host's fzf matcher, so pinyin and fuzzy tiers behave identically in the search box
/// and in the panel.
/// </summary>
internal static class ClipboardSearchSupport
{
    public static bool Matches(ClipboardEntry entry, string term, Func<string, string, bool> isMatch)
    {
        if (term.Length == 0) return true;
        return isMatch(term, entry.PlainPreview)
            || (entry.Content.Length > 0 && entry.Content != entry.PlainPreview && isMatch(term, entry.Content))
            || (entry.SourceProcess.Length > 0 && isMatch(term, entry.SourceProcess))
            || (entry.Note.Length > 0 && isMatch(term, entry.Note));
    }
}
