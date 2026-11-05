using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// The panel's window size and preview visibility, persisted as plugin settings so they survive
/// restarts. Values are clamped on read -- a hand-edited or stale setting must not be able to open
/// the panel off-screen.
/// </summary>
internal static class ClipboardPanelViewSettings
{
    private const string WidthKey = "PanelWidth";
    private const string HeightKey = "PanelHeight";
    private const string PreviewKey = "PanelPreviewVisible";
    private const string PreviewWidthKey = "PanelPreviewWidth";
    private const string LastTabKey = "PanelLastTab";
    private const string SortKey = "PanelSortByFrequency";
    private const double DefaultWidth = 780;
    private const double DefaultHeight = 620;
    private const double DefaultPreviewWidth = 300;
    private const double MaxSide = 2000;

    public static double Width() => Clamp(PluginSettingsService.GetSetting(ClipboardSettings.PluginId, WidthKey, DefaultWidth));

    public static double Height() => Clamp(PluginSettingsService.GetSetting(ClipboardSettings.PluginId, HeightKey, DefaultHeight));

    public static bool PreviewVisible() =>
        PluginSettingsService.GetSetting(ClipboardSettings.PluginId, PreviewKey, true);

    /// <summary>The preview column width the user dragged the splitter to. Clamp is tighter than
    /// the window's: the pane must keep the list readable at the window's minimum size.</summary>
    public static double PreviewWidth()
    {
        try { return Math.Clamp(Convert.ToDouble(PluginSettingsService.GetSetting(ClipboardSettings.PluginId, PreviewWidthKey, DefaultPreviewWidth)), 180, 600); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return DefaultPreviewWidth;
        }
    }

    public static void SavePreviewWidth(double width) =>
        PluginSettingsService.SetSetting(ClipboardSettings.PluginId, PreviewWidthKey, Math.Clamp(width, 180, 600));

    /// <summary>The tab the panel reopens on (mature managers reopen where you left off). Clamped
    /// to the known range -- a stale or hand-edited value falls back to All.</summary>
    public static int LastTab()
    {
        try { return Math.Clamp(Convert.ToInt32(PluginSettingsService.GetSetting(ClipboardSettings.PluginId, LastTabKey, 0)), 0, 4); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return 0;
        }
    }

    public static void SaveLastTab(int tab) =>
        PluginSettingsService.SetSetting(ClipboardSettings.PluginId, LastTabKey, tab);

    public static bool SortByFrequency() =>
        PluginSettingsService.GetSetting(ClipboardSettings.PluginId, SortKey, false);

    public static void SaveSortByFrequency(bool byFrequency) =>
        PluginSettingsService.SetSetting(ClipboardSettings.PluginId, SortKey, byFrequency);

    public static void Save(double width, double height, bool previewVisible)
    {
        PluginSettingsService.SetSetting(ClipboardSettings.PluginId, WidthKey, Clamp(width));
        PluginSettingsService.SetSetting(ClipboardSettings.PluginId, HeightKey, Clamp(height));
        PluginSettingsService.SetSetting(ClipboardSettings.PluginId, PreviewKey, previewVisible);
    }

    private static double Clamp(object? raw)
    {
        try { return Math.Clamp(Convert.ToDouble(raw), 420, MaxSide); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return DefaultWidth;
        }
    }
}
