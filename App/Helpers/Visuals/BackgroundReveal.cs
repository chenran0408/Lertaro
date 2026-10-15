using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Helpers.Visuals;

public static class BackgroundReveal
{
    public static readonly DependencyProperty ResourceProperty = DependencyProperty.RegisterAttached(
        "Resource", typeof(string), typeof(BackgroundReveal), new PropertyMetadata(null, Attach));
    public static void SetResource(DependencyObject target, string value) => target.SetValue(ResourceProperty, value);
    public static string GetResource(DependencyObject target) => (string)target.GetValue(ResourceProperty);

    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element || args.OldValue != null) return;
        void Refresh()
        {
            if (!element.Dispatcher.CheckAccess())
            {
                if (!element.Dispatcher.HasShutdownStarted) element.Dispatcher.BeginInvoke(Refresh);
                return;
            }
            var property = element is Border ? Border.BackgroundProperty : System.Windows.Controls.Panel.BackgroundProperty;
            if (BackgroundManager.IsCustom) element.SetCurrentValue(property, System.Windows.Media.Brushes.Transparent);
            else element.SetResourceReference(property, GetResource(element));
        }
        element.Loaded += (_, _) =>
        {
            BackgroundManager.Changed -= Refresh;
            BackgroundManager.Changed += Refresh;
            Refresh();
        };
        element.Unloaded += (_, _) => BackgroundManager.Changed -= Refresh;
    }
}
