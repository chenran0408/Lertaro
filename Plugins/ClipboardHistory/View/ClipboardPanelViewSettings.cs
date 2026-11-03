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
    private const double DefaultWidth = 780;
    private const double DefaultHeight = 620;
    private const double MaxSide = 2000;

    public static double Width() => Clamp(PluginSettingsService.GetSetting(ClipboardSettings.PluginId, WidthKey, DefaultWidth));

    public static double Height() => Clamp(PluginSettingsService.GetSetting(ClipboardSettings.PluginId, HeightKey, DefaultHeight));

    public static bool PreviewVisible() =>
        PluginSettingsService.GetSetting(ClipboardSettings.PluginId, PreviewKey, true);

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
