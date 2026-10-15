using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Helpers.Visuals;

// Only large content surfaces reveal the image; controls and popups retain their readable theme fill.
public static class BackgroundReveal
{
    public static readonly DependencyProperty ResourceProperty = DependencyProperty.RegisterAttached(
        "Resource", typeof(string), typeof(BackgroundReveal), new PropertyMetadata(null, Attach));
    public static void SetResource(DependencyObject target, string value) => target.SetValue(ResourceProperty, value);
    public static string GetResource(DependencyObject target) => (string)target.GetValue(ResourceProperty);

    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement border || args.OldValue != null) return;
        var background = target is Border ? Border.BackgroundProperty : System.Windows.Controls.Panel.BackgroundProperty;
        Window? owner = null;
        void Detach(object? sender, EventArgs args)
        {
            BackgroundManager.Changed -= Refresh;
            ThemeManager.Instance.ThemeChanged -= Refresh;
            if (owner != null) owner.Closed -= Detach;
            owner = null;
        }
        void Refresh()
        {
            if (!border.Dispatcher.CheckAccess())
            {
                if (!border.Dispatcher.HasShutdownStarted) border.Dispatcher.BeginInvoke(Refresh);
                return;
            }
            if (BackgroundManager.IsCustom) border.SetCurrentValue(background, System.Windows.Media.Brushes.Transparent);
            else border.SetResourceReference(background, GetResource(border));
        }
        border.Loaded += (_, _) =>
        {
            Detach(null, EventArgs.Empty);
            BackgroundManager.Changed += Refresh;
            ThemeManager.Instance.ThemeChanged += Refresh;
            owner = Window.GetWindow(border);
            if (owner != null) owner.Closed += Detach;
            Refresh();
        };
        border.Unloaded += Detach;
    }
}
