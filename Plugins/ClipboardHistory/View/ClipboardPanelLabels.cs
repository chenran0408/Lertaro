using System.Windows;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// Every user-visible string the panel owns, plus the time formatting for group headers and row
/// subtitles. Collected in one place so a language switch can refresh the open panel through the
/// same code path construction uses.
/// </summary>
internal static class ClipboardPanelLabels
{
    public static string Title() => TranslationService.Get("ClipboardHistory_PanelTitle");

    public static string TabLabel(ClipboardPanelTab tab) => tab switch
    {
        ClipboardPanelTab.Text => TranslationService.Get("ClipboardHistory_PanelTabText"),
        ClipboardPanelTab.Image => TranslationService.Get("ClipboardHistory_PanelTabImage"),
        ClipboardPanelTab.Files => TranslationService.Get("ClipboardHistory_PanelTabFiles"),
        ClipboardPanelTab.Favorites => TranslationService.Get("ClipboardHistory_PanelTabFavorites"),
        _ => TranslationService.Get("ClipboardHistory_PanelTabAll"),
    };

    public static string SearchWatermark() => TranslationService.Get("ClipboardHistory_PanelSearchWatermark");

    public static string HintBar() => TranslationService.Get("ClipboardHistory_PanelHint");

    public static string PinnedGroup() => TranslationService.Get("ClipboardHistory_PanelPinnedGroup");

    public static string Empty() => TranslationService.Get("ClipboardHistory_PanelEmpty");

    /// <summary>The date-group header: today, yesterday, or the local date.</summary>
    public static string DateGroup(long lastSeenMs)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(lastSeenMs).LocalDateTime;
        var today = DateTime.Today;
        if (local.Date == today) return TranslationService.Get("ClipboardHistory_PanelToday");
        if (local.Date == today.AddDays(-1)) return TranslationService.Get("ClipboardHistory_PanelYesterday");
        return local.ToString("yyyy/MM/dd");
    }

    /// <summary>The row subtitle's time half: clock time for anything recent enough to share a date
    /// with now, otherwise the short date. The group header already carries the date for old rows.</summary>
    public static string RowTime(long lastSeenMs)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(lastSeenMs).LocalDateTime;
        return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("MM/dd HH:mm");
    }

    public static string Subtitle(ClipboardEntry entry)
    {
        var source = entry.SourceProcess.Length > 0 ? entry.SourceProcess : "·";
        return $"{source}  ·  {RowTime(entry.LastSeenAtMs)}  ·  {entry.SeenCount}×";
    }

    /// <summary>Re-applies every string the panel shows. Called at construction and on a language
    /// switch so a label added here cannot be left out of one of the two paths.</summary>
    public static void ApplyTo(ClipboardPanelView view)
    {
        view.SearchWatermark.Text = SearchWatermark();
        view.HintBar.Text = HintBar();
        view.TabAll.Content = TabLabel(ClipboardPanelTab.All);
        view.TabText.Content = TabLabel(ClipboardPanelTab.Text);
        view.TabImage.Content = TabLabel(ClipboardPanelTab.Image);
        view.TabFiles.Content = TabLabel(ClipboardPanelTab.Files);
        view.TabFavorites.Content = TabLabel(ClipboardPanelTab.Favorites);
    }

    public static void SetEmpty(ClipboardPanelView view, bool visible)
    {
        view.EmptyText.Text = Empty();
        view.EmptyText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
