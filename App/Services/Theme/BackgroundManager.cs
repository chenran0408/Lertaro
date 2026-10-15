using Lertaro.Core;

namespace Lertaro.App.Services.Theme;

// Preview is deliberately separate from UserSettings: immediate theme saves must not persist a draft.
internal static class BackgroundManager
{
    private static BackgroundSettings? _preview;
    internal static BackgroundSettings Current => (_preview ?? UserSettings.Load().Background ?? new()).Normalize();
    internal static bool IsCustom => Current.Kind != BackgroundKind.Theme;
    internal static event Action? Changed;

    internal static void Preview(BackgroundSettings settings)
    {
        _preview = settings.Normalize();
        NotifyChanged();
    }

    internal static void EndPreview()
    {
        _preview = null;
        NotifyChanged();
    }

    private static void NotifyChanged()
    {
        var app = System.Windows.Application.Current;
        void UpdateOpacity()
        {
            if (app == null || app.Dispatcher.HasShutdownStarted) return;
            foreach (System.Windows.Window window in app.Windows)
            {
                if (!window.AllowsTransparency || window.Content is not System.Windows.UIElement content) continue;
                content.BeginAnimation(System.Windows.UIElement.OpacityProperty, null);
                content.Opacity = Helpers.Visuals.WindowEffectHelper.GetContentOpacity(ThemeManager.Instance.ActiveTheme?.WindowOpacity ?? 1.0);
            }
        }
        if (app != null && !app.Dispatcher.CheckAccess())
        {
            if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(UpdateOpacity);
        }
        else UpdateOpacity();
        Changed?.Invoke();
    }
}
