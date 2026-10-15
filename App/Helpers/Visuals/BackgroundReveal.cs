using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Helpers.Visuals;

// Blend large surfaces with the custom backdrop; controls and popups keep their theme fill.
public static class BackgroundReveal
{
    public static readonly DependencyProperty ResourceProperty = DependencyProperty.RegisterAttached(
        "Resource", typeof(string), typeof(BackgroundReveal), new PropertyMetadata(null, Attach));
    public static void SetResource(DependencyObject target, string value) => target.SetValue(ResourceProperty, value);
    public static string GetResource(DependencyObject target) => (string)target.GetValue(ResourceProperty);

    public static readonly DependencyProperty OpacityProperty = DependencyProperty.RegisterAttached(
        "Opacity", typeof(double), typeof(BackgroundReveal), new PropertyMetadata(0d, (target, _) =>
        {
            if (target is FrameworkElement element && GetResource(element) != null) Refresh(element);
        }), value => value is double opacity && double.IsFinite(opacity) && opacity is >= 0 and <= 1);
    public static void SetOpacity(DependencyObject target, double value) => target.SetValue(OpacityProperty, value);
    public static double GetOpacity(DependencyObject target) => (double)target.GetValue(OpacityProperty);

    private static void Refresh(FrameworkElement element)
    {
        var background = element is Border ? Border.BackgroundProperty : System.Windows.Controls.Panel.BackgroundProperty;
        var resource = GetResource(element);
        if (!BackgroundManager.IsCustom) element.SetResourceReference(background, resource);
        else
        {
            var brush = (element.TryFindResource(resource) as System.Windows.Media.Brush)?.CloneCurrentValue() ?? new SolidColorBrush();
            brush.Opacity *= GetOpacity(element);
            brush.Freeze();
            element.SetCurrentValue(background, brush);
        }
    }

    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement border || args.OldValue != null) return;
        Window? owner = null;
        void Detach(object? sender, EventArgs args)
        {
            BackgroundManager.Changed -= Refresh;
            ThemeManager.Instance.ThemeChanged -= Refresh;
            owner?.Closed -= Detach;
            owner = null;
        }
        void Refresh()
        {
            if (!border.Dispatcher.CheckAccess())
            {
                if (!border.Dispatcher.HasShutdownStarted) border.Dispatcher.BeginInvoke(Refresh);
                return;
            }
            BackgroundReveal.Refresh(border);
        }
        border.Loaded += (_, _) =>
        {
            Detach(null, EventArgs.Empty);
            BackgroundManager.Changed += Refresh;
            ThemeManager.Instance.ThemeChanged += Refresh;
            owner = Window.GetWindow(border);
            owner?.Closed += Detach;
            Refresh();
        };
        border.Unloaded += Detach;
    }
}
