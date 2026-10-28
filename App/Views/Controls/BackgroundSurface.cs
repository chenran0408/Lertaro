using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Lertaro.App.Services.Theme;
using Lertaro.Core;

namespace Lertaro.App.Views.Controls;

// A picture or shared video over the theme background. Input and foreground content are unaffected.
public sealed class BackgroundSurface : Grid
{
    public static readonly DependencyProperty UseTransparentBaseProperty = DependencyProperty.Register(
        nameof(UseTransparentBase), typeof(bool), typeof(BackgroundSurface),
        new PropertyMetadata(false, OnUseTransparentBaseChanged));

    public bool UseTransparentBase
    {
        get => (bool)GetValue(UseTransparentBaseProperty);
        set => SetValue(UseTransparentBaseProperty, value);
    }

    private readonly Border _image = new() { IsHitTestVisible = false };
    private int _generation;
    private string? _loadedPath;
    private readonly BackgroundVideoPlayback _video = BackgroundVideoPlayback.Current;
    private Window? _host;

    public BackgroundSurface()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        ApplyBaseBackground();
        Children.Add(_image);
        Loaded += (_, _) =>
        {
            BackgroundManager.Changed -= Refresh;
            BackgroundManager.Changed += Refresh;
            ThemeManager.Instance.ThemeChanged -= Refresh;
            ThemeManager.Instance.ThemeChanged += Refresh;
            _host = Window.GetWindow(this);
            if (_host != null) _host.StateChanged += OnWindowStateChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            BackgroundManager.Changed -= Refresh;
            ThemeManager.Instance.ThemeChanged -= Refresh;
            ++_generation;
            if (_host != null) _host.StateChanged -= OnWindowStateChanged;
            _host = null;
            _video.Release(this);
            if (_image.Background is DrawingBrush) { _image.Background = null; _loadedPath = null; }
        };
        IsVisibleChanged += (_, _) => UpdateVideoActivity();
    }

    private bool VideoIsActive => IsLoaded && IsVisible && _host?.WindowState != WindowState.Minimized && BackgroundManager.Current.Opacity > 0;
    private void OnWindowStateChanged(object? sender, EventArgs args) => UpdateVideoActivity();
    private void UpdateVideoActivity() => _video.SetActive(this, VideoIsActive);

    private void ApplyBaseBackground()
    {
        if (UseTransparentBase) ClearValue(BackgroundProperty);
        else SetResourceReference(BackgroundProperty, "ContentBg");
    }

    private static void OnUseTransparentBaseChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        => ((BackgroundSurface)target).ApplyBaseBackground();

    private async void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted) await Dispatcher.InvokeAsync(Refresh);
            return;
        }
        var generation = ++_generation;
        var settings = BackgroundManager.Current;
        var isImage = settings.Kind == BackgroundKind.Image;
        Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        _image.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
        if (!isImage)
        {
            _video.Release(this);
            _image.Background = null;
            _loadedPath = null;
            return;
        }
        if (BackgroundVideoPlayback.IsVideoPath(settings.ImagePath))
        {
            if (_image.Background is DrawingBrush currentVideo && _loadedPath == settings.ImagePath)
            {
                currentVideo.Opacity = settings.Opacity;
                currentVideo.Stretch = settings.FitImage ? Stretch.Uniform : Stretch.UniformToFill;
                UpdateVideoActivity();
                return;
            }
            _video.Release(this);
            _image.Background = null;
            _loadedPath = null;
            var readable = await BackgroundVideoPlayback.IsReadableAsync(settings.ImagePath).ConfigureAwait(false);
            if (Dispatcher.HasShutdownStarted) return;
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (generation != _generation || !IsLoaded || !readable) return;
                    _loadedPath = settings.ImagePath;
                    _image.Background = new DrawingBrush(_video.Drawing)
                    {
                        Opacity = settings.Opacity,
                        Stretch = settings.FitImage ? Stretch.Uniform : Stretch.UniformToFill
                    };
                    _video.Acquire(this, settings.ImagePath, VideoIsActive);
                });
            }
            catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted) { }
            return;
        }
        _video.Release(this);
        if (_image.Background is DrawingBrush) { _image.Background = null; _loadedPath = null; }
        if (_image.Background is ImageBrush current)
        {
            current.Opacity = settings.Opacity;
            current.Stretch = settings.FitImage ? Stretch.Uniform : Stretch.UniformToFill;
            _image.Effect = CreateImageEffect(settings.Blur);
            if (_loadedPath == settings.ImagePath) return;
        }
        var image = await BackgroundImageCache.LoadAsync(settings.ImagePath).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted) return;
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != _generation || !IsLoaded) return;
                _loadedPath = image == null ? null : settings.ImagePath;
                _image.Background = image == null ? null : new ImageBrush(image)
                {
                    Opacity = settings.Opacity,
                    Stretch = settings.FitImage ? Stretch.Uniform : Stretch.UniformToFill
                };
                _image.Effect = CreateImageEffect(settings.Blur);
            });
        }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted) { }
    }

    private static BlurEffect? CreateImageEffect(double blur)
        => blur > 0 ? new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Performance } : null;
}
