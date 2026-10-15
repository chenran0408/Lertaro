using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lertaro.App.Services.Theme;
using Lertaro.Core;

namespace Lertaro.App.Views.Controls;

// A plain image over the theme background. Input and foreground content are unaffected.
public sealed class BackgroundSurface : Grid
{
    private readonly Border _image = new() { IsHitTestVisible = false };
    private int _generation;
    private string? _loadedPath;

    public BackgroundSurface()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        SetResourceReference(BackgroundProperty, "ContentBg");
        Children.Add(_image);
        Loaded += (_, _) =>
        {
            BackgroundManager.Changed -= Refresh;
            BackgroundManager.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            BackgroundManager.Changed -= Refresh;
            ++_generation;
        };
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
        Visibility = settings.Kind == BackgroundKind.Image ? Visibility.Visible : Visibility.Collapsed;
        if (settings.Kind != BackgroundKind.Image)
        {
            _image.Background = null;
            _loadedPath = null;
            return;
        }
        if (_image.Background is ImageBrush current)
        {
            current.Opacity = settings.Opacity;
            current.Stretch = settings.FitImage ? Stretch.Uniform : Stretch.UniformToFill;
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
            });
        }
        catch (TaskCanceledException) when (Dispatcher.HasShutdownStarted) { }
    }
}
