using System.Windows;

namespace Lertaro.App.Helpers.Visuals;

public static class WindowEffectHelper
{
    public static double GetContentOpacity(double themeOpacity)
        => Services.Theme.BackgroundManager.IsCustom ? 1.0 : themeOpacity;

    public static void ApplyThemeEffects(Window window, PluginSdk.Abstractions.ITheme theme)
    {
        var opacity = GetContentOpacity(theme.WindowOpacity);

        // Apply Window Opacity (WPF side) only if AllowsTransparency is enabled on the window
        if (window.AllowsTransparency && window.Content is FrameworkElement rootElement)
        {
            rootElement.Opacity = opacity;
        }
    }
}
