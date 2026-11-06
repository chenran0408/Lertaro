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

    public static string EmptyHints() => TranslationService.Get("ClipboardHistory_PanelEmptyHints");

    /// <summary>The row hover surface: the whole text (bounded so a huge clip cannot stall the
    /// tooltip), image dimensions, or the path list. ponytail: 2000 chars is a flat display cap,
    /// not a storage one -- the preview pane and paste stay complete; if someone truly reads past
    /// it, the upgrade is a scrolling tooltip, not a bigger number.</summary>
    public static string Tooltip(ClipboardEntry entry) => entry.Kind switch
    {
        ClipboardEntryKind.Image => $"{entry.ImageWidth}\u00d7{entry.ImageHeight}",
        ClipboardEntryKind.FilePaths => string.Join("\n", entry.PathList),
        _ => entry.Content.Length > 2000 ? entry.Content[..2000] + "\u2026" : entry.Content,
    };

    public static string PreviewTextTitle() => TranslationService.Get("ClipboardHistory_PanelPreviewText");

    public static string PreviewFilesTitle() => TranslationService.Get("ClipboardHistory_PanelPreviewFiles");

    public static string PreviewHidden() => TranslationService.Get("ClipboardHistory_PanelPreviewHidden");

    public static string PreviewChip() => TranslationService.Get("ClipboardHistory_PanelPreviewChip");

    public static string NoteLabel() => TranslationService.Get("ClipboardHistory_PanelNote");

    public static string NotePlaceholder() => TranslationService.Get("ClipboardHistory_PanelNotePlaceholder");

    public static string PausedUntil(string time) => TranslationService.Format("ClipboardHistory_PanelPausedUntil", time);

    /// <summary>The date-group header: today, yesterday, or the local date.</summary>
    public static string DateGroup(long lastSeenMs)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(lastSeenMs).LocalDateTime;
        var today = DateTime.Today;
        if (local.Date == today) return TranslationService.Get("ClipboardHistory_PanelToday");
        if (local.Date == today.AddDays(-1)) return TranslationService.Get("ClipboardHistory_PanelYesterday");
        return local.ToString("yyyy/MM/dd");
    }

    /// <summary>The row subtitle's time half, relative like every mature clipboard tool: 刚刚 /
    /// N分钟前 / N小时前 inside the last day, then the clock time for anything on a previous day
    /// (the group header already carries that date).</summary>
    public static string RowTime(long lastSeenMs) => RelativeTime(
        DateTimeOffset.FromUnixTimeMilliseconds(lastSeenMs), DateTimeOffset.Now);

    /// <summary>Pure relative-time formatting over an injected clock so the thresholds are testable.</summary>
    public static string RelativeTime(DateTimeOffset local, DateTimeOffset now)
    {
        var elapsed = now - local;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed.TotalMinutes < 1) return TranslationService.Get("ClipboardHistory_TimeNow");
        if (elapsed.TotalHours < 1)
            return TranslationService.Format("ClipboardHistory_TimeMinutesAgo", (int)elapsed.TotalMinutes);
        if (local.Date == now.LocalDateTime.Date)
            return TranslationService.Format("ClipboardHistory_TimeHoursAgo", (int)elapsed.TotalHours);
        return local.Date == now.LocalDateTime.Date.AddDays(-1)
            ? TranslationService.Get("ClipboardHistory_PanelYesterday") + " " + local.ToString("HH:mm")
            : local.ToString("MM/dd HH:mm");
    }

    public static string Subtitle(ClipboardEntry entry)
    {
        var source = entry.SourceProcess.Length > 0 ? entry.SourceProcess : "·";
        // The pencil tells the user a note exists without opening the preview: the note is
        // invisible in the row by design, and an annotated row that looks identical to the rest
        // might as well not have one.
        return $"{source}  ·  {RowTime(entry.LastSeenAtMs)}  ·  {entry.SeenCount}×{(entry.Note.Length > 0 ? "  ·  ✎" : "")}";
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
        view.PreviewToggle.Content = PreviewChip();
        view.PreviewToggle.ToolTip = TranslationService.Get("ClipboardHistory_PanelTogglePreview");
    }

    public static void SetEmpty(ClipboardPanelView view, bool visible)
    {
        view.EmptyText.Text = visible ? $"{Empty()}\n\n{EmptyHints()}" : string.Empty;
        view.EmptyText.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
