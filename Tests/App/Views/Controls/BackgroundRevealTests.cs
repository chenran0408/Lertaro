using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.Theme;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass, DoNotParallelize]
public sealed class BackgroundRevealTests
{
    [TestCleanup] public void Cleanup() => BackgroundManager.EndPreview();

    [StaTestMethod]
    [DataRow(BackgroundKind.Gradient)]
    [DataRow(BackgroundKind.Image)]
    [DataRow(BackgroundKind.Material)]
    public void SurfaceBlendsPaletteWithoutFadingContentAndRestoresDynamicResource(BackgroundKind kind)
    {
        BackgroundManager.Preview(new() { Kind = BackgroundKind.Theme });
        var original = new SolidColorBrush(Colors.DarkSlateBlue);
        original.Freeze();
        var surface = new Border { Child = new TextBlock { Text = "Readable content" } };
        surface.Resources["Surface"] = original;
        BackgroundReveal.SetResource(surface, "Surface");
        BackgroundReveal.SetOpacity(surface, .65);
        var window = new Window { Content = surface, Width = 200, Height = 100 };
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreSame(original, surface.Background);
            BackgroundManager.Preview(new() { Kind = kind });
            var tinted = Assert.IsInstanceOfType<SolidColorBrush>(surface.Background);
            Assert.AreEqual(original.Color, tinted.Color);
            Assert.AreEqual(.65, tinted.Opacity);
            Assert.AreEqual(1d, original.Opacity);
            Assert.AreEqual(1d, surface.Opacity);
            Assert.AreEqual(1d, surface.Child.Opacity);
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Theme });
            Assert.AreSame(original, surface.Background);
            var replacement = new SolidColorBrush(Colors.Linen);
            surface.Resources["Surface"] = replacement;
            Assert.AreSame(replacement, surface.Background);
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public void BackgroundUsesThemePaletteForCustomTintAndFallback()
    {
        BackgroundManager.Preview(new() { Kind = BackgroundKind.Gradient, Tint = .4 });
        var surface = new Lertaro.App.Views.Controls.BackgroundSurface();
        surface.Resources["ContentBg"] = new SolidColorBrush(Colors.DarkSlateBlue);
        var window = new Window { Content = surface, Width = 200, Height = 100 };
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(Colors.DarkSlateBlue, Assert.IsInstanceOfType<SolidColorBrush>(surface.Background).Color);
            var tint = Assert.IsInstanceOfType<SolidColorBrush>(((Border)surface.Children[1]).Background);
            Assert.AreEqual(Colors.DarkSlateBlue, tint.Color);
            Assert.AreEqual(.4, tint.Opacity);
            surface.Resources["ContentBg"] = new SolidColorBrush(Colors.Linen);
            BackgroundManager.Preview(new() { Kind = BackgroundKind.Gradient, Tint = .7 });
            tint = Assert.IsInstanceOfType<SolidColorBrush>(((Border)surface.Children[1]).Background);
            Assert.AreEqual(Colors.Linen, tint.Color);
            Assert.AreEqual(.7, tint.Opacity);
        }
        finally { window.Close(); }
    }
}
