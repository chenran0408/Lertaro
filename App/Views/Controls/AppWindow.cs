using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace Lertaro.App.Views.Controls;

/// <summary>
/// Shared opaque top-level window base. Theme values remain live through application resource
/// references so windows follow the built-in Light and Dark dictionaries without a glass runtime.
/// </summary>
public class AppWindow : Window
{
    public static readonly DependencyProperty ForegroundBrushProperty = DependencyProperty.Register(
        nameof(ForegroundBrush), typeof(MediaBrush), typeof(AppWindow));

    public static readonly DependencyProperty SecondaryForegroundBrushProperty = DependencyProperty.Register(
        nameof(SecondaryForegroundBrush), typeof(MediaBrush), typeof(AppWindow));

    public static readonly DependencyProperty PanelBackgroundBrushProperty = DependencyProperty.Register(
        nameof(PanelBackgroundBrush), typeof(MediaBrush), typeof(AppWindow),
        new PropertyMetadata(MediaBrushes.Transparent));

    public static readonly DependencyProperty PanelBorderBrushProperty = DependencyProperty.Register(
        nameof(PanelBorderBrush), typeof(MediaBrush), typeof(AppWindow));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(MediaBrush), typeof(AppWindow));

    public static readonly DependencyProperty PanelCornerRadiusProperty = DependencyProperty.Register(
        nameof(PanelCornerRadius), typeof(CornerRadius), typeof(AppWindow),
        new PropertyMetadata(new CornerRadius(40)));

    public MediaBrush? ForegroundBrush
    {
        get => (MediaBrush?)GetValue(ForegroundBrushProperty);
        private set => SetValue(ForegroundBrushProperty, value);
    }

    public MediaBrush? SecondaryForegroundBrush
    {
        get => (MediaBrush?)GetValue(SecondaryForegroundBrushProperty);
        private set => SetValue(SecondaryForegroundBrushProperty, value);
    }

    public MediaBrush? PanelBackgroundBrush
    {
        get => (MediaBrush?)GetValue(PanelBackgroundBrushProperty);
        private set => SetValue(PanelBackgroundBrushProperty, value);
    }

    public MediaBrush? PanelBorderBrush
    {
        get => (MediaBrush?)GetValue(PanelBorderBrushProperty);
        private set => SetValue(PanelBorderBrushProperty, value);
    }

    public MediaBrush? AccentBrush
    {
        get => (MediaBrush?)GetValue(AccentBrushProperty);
        private set => SetValue(AccentBrushProperty, value);
    }

    public CornerRadius PanelCornerRadius
    {
        get => (CornerRadius)GetValue(PanelCornerRadiusProperty);
        protected set => SetValue(PanelCornerRadiusProperty, value);
    }

    public AppWindow()
    {
        AllowsTransparency = false;
        SetResourceReference(BackgroundProperty, "ContentBg");
        SetResourceReference(ForegroundProperty, "TextPrimary");
        SetResourceReference(ForegroundBrushProperty, "TextPrimary");
        SetResourceReference(SecondaryForegroundBrushProperty, "TextSecondary");
        SetResourceReference(PanelBorderBrushProperty, "WindowBorderBrush");
        SetResourceReference(AccentBrushProperty, "AccentBlue");
    }
}
