using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Lertaro.App.Helpers.Visuals;
using Lertaro.Core;

namespace Lertaro.App.Services.Theme;

// Capture and refraction are confined to the background layer; no input handlers are installed.
internal sealed class LiquidGlassSession : IDisposable
{
    private readonly FrameworkElement _surface;
    private readonly Border _paint;
    private readonly Window _window;
    private readonly nint _handle;
    private readonly uint _originalAffinity;
    private CancellationTokenSource? _capture;
    private bool _disposed, _failed;
    private WriteableBitmap? _bitmap;
    private readonly LiquidGlassEffect _effect = new();
    internal bool IsCapturing => _capture != null;
    internal LiquidGlassSession(FrameworkElement surface, Border paint, Window window)
    {
        _surface = surface; _paint = paint; _window = window;
        _handle = new WindowInteropHelper(window).Handle;
        if (_handle == 0 || !GetWindowDisplayAffinity(_handle, out _originalAffinity)) throw new InvalidOperationException("Window capture affinity unavailable.");
        _window.IsVisibleChanged += VisibilityChanged;
        _window.StateChanged += StateChanged;
        _surface.SizeChanged += SizeChanged;
        Refresh();
    }
    internal void Refresh()
    {
        if (_disposed || _failed) return;
        if (!_window.IsVisible || _window.WindowState == WindowState.Minimized) { Stop(); return; }
        if (_capture != null)
        {
            if (_bitmap != null) { _paint.Background = System.Windows.Media.Brushes.White; _paint.Effect = _effect; }
            return;
        }
        // Exclude only this HWND to prevent feeding the rendered glass back into its own source.
        if (!SetWindowDisplayAffinity(_handle, 0x11)) { Fail(new InvalidOperationException("Cannot exclude glass from desktop capture.")); return; }
        var cancellation = _capture = new CancellationTokenSource();
        _ = RunAsync(cancellation);
    }
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) => Refresh();
    private void StateChanged(object? sender, EventArgs args) => Refresh();
    private void SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_failed && GetRegion(_surface).Width > 0) { _failed = false; Refresh(); }
    }
    private async Task RunAsync(CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        try
        {
            await Task.Run(async () =>
            {
                using var capture = new GlassBackdropCapture();
                byte[] previous = [];
                (int Left, int Top, int Width, int Height) previousRegion = default;
                while (!token.IsCancellationRequested)
                {
                    var frame = await _surface.Dispatcher.InvokeAsync(() => GetRegion(_surface), System.Windows.Threading.DispatcherPriority.Background, token);
                    if (frame.Width == 0) throw new InvalidOperationException("Window capture area exceeds the supported size.");
                    var pixels = capture.Read(frame.Left, frame.Top, frame.Width, frame.Height);
                    var changed = frame != previousRegion || !pixels.AsSpan().SequenceEqual(previous);
                    if (changed)
                    {
                        if (previous.Length != pixels.Length) previous = new byte[pixels.Length];
                        pixels.CopyTo(previous, 0); previousRegion = frame;
                        await _surface.Dispatcher.InvokeAsync(() =>
                        {
                            if (token.IsCancellationRequested || _disposed) return;
                            Paint(frame, pixels);
                        }, System.Windows.Threading.DispatcherPriority.Background, token);
                    }
                    // ponytail: bounded GDI sampling at 12.5 Hz; a GPU capture backend can replace this if desktop sampling dominates.
                    await Task.Delay(80, token);
                }
            }, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested && !_disposed) Fail(ex); }
        finally { lifetime.Dispose(); }
    }
    internal static (int Left, int Top, int Width, int Height) GetRegion(FrameworkElement surface)
    {
        if (!surface.IsLoaded || surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return default;
        var a = surface.PointToScreen(new System.Windows.Point(0, 0));
        var b = surface.PointToScreen(new System.Windows.Point(surface.ActualWidth, surface.ActualHeight));
        return Region(a.X, a.Y, b.X, b.Y);
    }
    internal static (int Left, int Top, int Width, int Height) Region(double left, double top, double right, double bottom)
    {
        if (!double.IsFinite(left + top + right + bottom) || right <= left || bottom <= top
            || Math.Abs(left) > 1_000_000 || Math.Abs(top) > 1_000_000 || right - left > 10_000 || bottom - top > 10_000) return default;
        int x = (int)Math.Floor(left) - 32, y = (int)Math.Floor(top) - 32;
        int width = (int)Math.Ceiling(right) + 32 - x, height = (int)Math.Ceiling(bottom) + 32 - y;
        return (long)width * height > 5_000_000 ? default : (x, y, width, height);
    }
    private void Paint((int Left, int Top, int Width, int Height) region, byte[] pixels)
    {
        if (_bitmap == null || _bitmap.PixelWidth != region.Width || _bitmap.PixelHeight != region.Height)
        {
            _bitmap = new WriteableBitmap(region.Width, region.Height, 96, 96, PixelFormats.Bgr32, null);
            _effect.Input = new ImageBrush(_bitmap) { Stretch = Stretch.Fill };
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, region.Width, region.Height), pixels, region.Width * 4, 0);
        var position = _surface.PointToScreen(new System.Windows.Point());
        var dpi = VisualTreeHelper.GetDpi(_surface);
        var profile = LiquidGlassProfile.Resolve(BackgroundManager.Current.GlassLevel);
        _effect.Bounds = new Point4D((position.X - region.Left) / region.Width, (position.Y - region.Top) / region.Height,
            _surface.ActualWidth * dpi.DpiScaleX / region.Width, _surface.ActualHeight * dpi.DpiScaleY / region.Height);
        _effect.Shape = new Point4D(_surface.ActualWidth, _surface.ActualHeight, 10, profile.EdgeWidth);
        _effect.Optics = new Point4D(profile.Refraction, profile.Blur, 0, 0);
        _paint.Background = System.Windows.Media.Brushes.White;
        _paint.Effect = _effect;
    }
    private void Stop()
    {
        _capture?.Cancel(); _capture = null;
        _paint.Effect = null; _paint.Background = null;
        _bitmap = null; _effect.ClearValue(LiquidGlassEffect.InputProperty);
        if (_handle != 0 && GetWindowDisplayAffinity(_handle, out var current) && current == 0x11)
            SetWindowDisplayAffinity(_handle, _originalAffinity);
    }
    private void Fail(Exception ex)
    {
        _failed = true; Stop();
        Logger.Log($"[LiquidGlass] Using system glass fallback: {ex.Message}", LogLevel.Warn);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _window.IsVisibleChanged -= VisibilityChanged; _window.StateChanged -= StateChanged;
        _surface.SizeChanged -= SizeChanged;
        Stop();
    }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
}
