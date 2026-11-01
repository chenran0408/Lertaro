using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory;

public sealed class ClipboardInstantProvider : IInstantResultProvider
{
    internal const string ClipboardIcon = "M16 2h-2.18C13.4.84 12.3 0 11 0S8.6.84 8.18 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V4a2 2 0 0 0-2-2h-2zm-5 0a1 1 0 1 1 0 2 1 1 0 0 1 0-2zm7 18H6V4h2v3h6V4h4v16z";
    public string Name => TranslationService.Get("ClipboardHistory_TabName");
    public IReadOnlyList<string> QueryTriggerKeywords => [ClipboardSettings.TriggerKeyword];

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (!ClipboardSettings.Enabled || !TriggerWord.TryMatch(query, ClipboardSettings.TriggerKeyword, out var term))
            return [];
        return MatchEntries(ClipboardRuntime.Instance.Entries, term)
            .Select(entry => (InstantResultItem)new ClipboardSearchResult(entry)).ToArray();
    }

    internal static IEnumerable<ClipboardEntry> MatchEntries(IEnumerable<ClipboardEntry> entries, string term) =>
        entries.Where(entry => entry.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Pinned)
            .ThenByDescending(entry => entry.LastSeenAtMs)
            .ThenByDescending(entry => entry.Id);

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (!TriggerWord.TryMatch(query, ClipboardSettings.TriggerKeyword, out var term) || term.Length == 0)
            return null;
        return Highlight(text, term);
    }

    internal static bool[] Highlight(string text, string term)
    {
        var mask = new bool[text.Length];
        if (term.Length == 0) return mask;
        for (var start = 0; start <= text.Length - term.Length;)
        {
            var index = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;
            Array.Fill(mask, true, index, term.Length);
            start = index + 1;
        }
        return mask;
    }
}
