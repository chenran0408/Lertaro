using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lertaro.App.Services.Theme;
using Lertaro.App.Views.Controls;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass, DoNotParallelize]
public sealed class BackgroundSurfaceTests
{
    [StaTestMethod]
    public void OpacityChangesOnlyTheImageWithoutReloadingAndMissingImagesKeepTheTheme()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
        var surface = new BackgroundSurface();
        surface.Resources["ContentBg"] = Brushes.Black;
        var window = new Window { Content = surface, Width = 150, Height = 150, ShowActivated = false };
        var settings = new BackgroundSettings { Kind = BackgroundKind.Image, ImagePath = path, Opacity = .25 };
        try
        {
            BackgroundManager.Preview(settings); window.Show();
            var paint = (Border)surface.Children[0];
            PumpUntil(() => paint.Background is ImageBrush);
            var brush = Assert.IsInstanceOfType<ImageBrush>(paint.Background);
            Assert.AreEqual(.25, brush.Opacity); Assert.IsNull(paint.Effect); Assert.IsNull(surface.Effect);
            Assert.IsFalse(surface.IsHitTestVisible);
            Assert.AreEqual(64, Red(surface), 1);
            BackgroundManager.Preview(settings with { Opacity = .8, FitImage = true });
            Assert.AreSame(brush, paint.Background); Assert.AreEqual(.8, brush.Opacity);
            Assert.AreEqual(Stretch.Uniform, brush.Stretch);
            Assert.AreEqual(204, Red(surface), 1);
            BackgroundManager.Preview(settings with { Opacity = .8, FitImage = true, Blur = 3.5 });
            Assert.AreSame(brush, paint.Background);
            Assert.AreEqual(3.5, Assert.IsInstanceOfType<BlurEffect>(paint.Effect).Radius);
            BackgroundManager.Preview(settings with { Opacity = .8, FitImage = true, Blur = 0 });
            Assert.IsNull(paint.Effect);
            BackgroundManager.Preview(settings with { ImagePath = path + ".missing" });
            PumpUntil(() => paint.Background == null);
            Assert.AreEqual(0, Red(surface));
            Assert.AreSame(Brushes.Black, surface.Background);
        }
        finally { window.Close(); BackgroundManager.EndPreview(); File.Delete(path); }
    }

    [StaTestMethod]
    public void TransparentBaseCanBeToggledAfterTheControlIsCreated()
    {
        var surface = new BackgroundSurface();
        surface.Resources["ContentBg"] = Brushes.Black;
        var window = new Window { Content = surface, ShowActivated = false };
        try
        {
            surface.UseTransparentBase = true;
            Assert.IsNull(surface.Background);
            surface.UseTransparentBase = false;
            Assert.AreSame(Brushes.Black, surface.Background);
            surface.UseTransparentBase = true;
            Assert.IsNull(surface.Background);
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public void LegacySolidAndGradientPresetsNoLongerPaintOverTheGlassSurface()
    {
        var surface = new BackgroundSurface();
        surface.Resources["ContentBg"] = Brushes.Black;
        var window = new Window { Content = surface, Width = 150, Height = 150, ShowActivated = false };
        try
        {
            var solid = new BackgroundSettings
            {
                Kind = BackgroundKind.Solid, FirstColor = "#123456", Tint = .4
            }.Normalize();
            Assert.AreEqual(BackgroundKind.Theme, solid.Kind);
            BackgroundManager.Preview(solid);
            window.Show();
            var background = (Border)surface.Children[0];
            Assert.IsNull(background.Background);
            Assert.IsFalse(BackgroundManager.IsCustom);

            var gradient = new BackgroundSettings
            {
                Kind = BackgroundKind.Gradient, FirstColor = "#112233", SecondColor = "#445566", Angle = 90, Tint = 0
            }.Normalize();
            Assert.AreEqual(BackgroundKind.Theme, gradient.Kind);
            BackgroundManager.Preview(gradient);
            Assert.IsNull(background.Background);
            Assert.IsFalse(BackgroundManager.IsCustom);
        }
        finally { window.Close(); BackgroundManager.EndPreview(); }
    }

    private static byte Red(FrameworkElement surface)
    {
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface); var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)surface.ActualWidth / 2, (int)surface.ActualHeight / 2, 1, 1), pixel, 4, 0); return pixel[2];
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(3);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow >= deadline) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.IsTrue(condition(), "Image preview must finish without blocking the dispatcher.");
    }
}
