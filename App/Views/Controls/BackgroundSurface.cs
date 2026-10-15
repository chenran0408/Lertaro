using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Lertaro.App.Services.Theme;
using Lertaro.Core;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Lertaro.App.Views.Controls;

// A background-only layer: effects never blur text or participate in hit testing or dragging.
public sealed class BackgroundSurface : Grid
{
    private readonly Border _paint = new() { IsHitTestVisible = false };
    private readonly Border _tint = new() { IsHitTestVisible = false };
    private int _generation;
    private Window? _owner;
    private LiquidGlassSession? _liquid;

    public BackgroundSurface()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Children.Add(_paint);
        Children.Add(_tint);
        Loaded += (_, _) =>
        {
            Detach();
            BackgroundManager.Changed += Refresh;
            ThemeManager.Instance.ThemeChanged += Refresh;
            _owner = Window.GetWindow(this);
            _owner?.Closed += OwnerClosed;
            Refresh();
        };
        Unloaded += (_, _) => Detach();
    }

    private void OwnerClosed(object? sender, EventArgs args) => Detach();

    private void Detach()
    {
        _liquid?.Dispose(); _liquid = null;
        BackgroundManager.Changed -= Refresh;
        ThemeManager.Instance.ThemeChanged -= Refresh;
        _owner?.Closed -= OwnerClosed;
        _owner = null;
        ++_generation;
    }

    private async void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) await Dispatcher.InvokeAsync(Refresh);
            return;
        }
        var generation = ++_generation;
        var settings = BackgroundManager.Current;
        var liquid = settings.Kind == BackgroundKind.Material && settings.Material == WindowMaterial.LiquidGlass;
        if (!liquid) { _liquid?.Dispose(); _liquid = null; }
        Background = null;
        _paint.Effect = null;
        _paint.Background = null;
        _tint.Background = null;
        var window = Window.GetWindow(this);
        var material = window != null && Helpers.Visuals.NativeMaterialHost.Refresh(window);
        if (settings.Kind == BackgroundKind.Theme) return;
        var dark = ThemeManager.Instance.ActiveTheme?.IsDark == true;
        // Use the selected palette, including plugin themes, for tint and fallback surfaces.
        var baseColor = (TryFindResource("ContentBg") as SolidColorBrush)?.Color
            ?? (dark ? Color.FromRgb(30, 32, 38) : Color.FromRgb(249, 250, 252));
        Background = material ? System.Windows.Media.Brushes.Transparent : new SolidColorBrush(baseColor);
        Brush? brush = settings.Kind switch
        {
            BackgroundKind.Solid => new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.FirstColor)),
            BackgroundKind.Gradient => CreateGradient(settings),
            _ => null
        };
        if (settings.Kind == BackgroundKind.Image)
        {
            var image = await BackgroundImageCache.LoadAsync(settings.ImagePath);
            if (generation != _generation) return;
            if (image != null) brush = new ImageBrush(image)
            {
                Stretch = settings.FitImage ? Stretch.Uniform : Stretch.UniformToFill
            };
            if (settings.Blur > 0 && brush != null)
                _paint.Effect = new BlurEffect { Radius = settings.Blur, RenderingBias = RenderingBias.Performance };
        }
        _paint.Background = brush;
        _tint.Background = new SolidColorBrush(baseColor) { Opacity = settings.Tint };
        // Inline previews use the normal fallback; only the full-window background owns a capture session.
        if (liquid && material && (RenderCapability.Tier >> 16) >= 2 && window != null
            && ActualWidth >= window.ActualWidth * .7 && ActualHeight >= window.ActualHeight * .5)
        {
            try { (_liquid ??= new LiquidGlassSession(this, _paint, window)).Refresh(); }
            catch (Exception ex) { Logger.Log($"[LiquidGlass] Cannot start background: {ex.Message}", LogLevel.Warn); }
        }
    }

    internal static LinearGradientBrush CreateGradient(BackgroundSettings settings)
    {
        // The angle constructor starts at (0,0), so obtuse directions paint mostly one color.
        // Center the vector inside the relative bounding box instead.
        var radians = settings.Angle * Math.PI / 180;
        var x = Math.Cos(radians) / 2;
        var y = Math.Sin(radians) / 2;
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(settings.FirstColor),
            (Color)ColorConverter.ConvertFromString(settings.SecondColor),
            new System.Windows.Point(.5 - x, .5 - y), new System.Windows.Point(.5 + x, .5 + y));
        brush.Freeze();
        return brush;
    }
}
