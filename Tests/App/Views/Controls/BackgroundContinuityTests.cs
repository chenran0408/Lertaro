using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.Theme;
using Lertaro.App.Views.QuickSearchWindow;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Controls;

[TestClass, DoNotParallelize]
public sealed class BackgroundContinuityTests
{
    [StaTestMethod]
    [DataRow("Light", false)]
    [DataRow("Dark", true)]
    public void LaunchPanelAndSettingsCardRevealOneSharedBackgroundAndRestoreTheme(string theme, bool dark)
    {
        var root = new Grid { Background = Brushes.Lime };
        root.Resources.MergedDictionaries.Add(new BuiltInTheme(theme, dark).GetResources());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(200) });
        var header = new Border();
        BackgroundReveal.SetResource(header, "HeaderBg");
        root.Children.Add(header);
        var panel = new QuickSearchLaunchPanel { Height = 200 };
        Grid.SetRow(panel, 1); root.Children.Add(panel);
        var card = new Border { Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top };
        card.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles.xaml") });
        card.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles/Windows/SettingsWindow.xaml") });
        card.Style = (Style)card.FindResource("SettingsCard");
        root.Children.Add(card);
        var window = new Window { Content = root, Width = 420, Height = 330, ShowActivated = false };
        try
        {
            BackgroundManager.Preview(new BackgroundSettings());
            window.Show(); Pump();
            Assert.AreEqual(Colors.Lime, Pixel(root, 200, 40), "The header must expose GlassDesk's sampled material surface.");
            Assert.AreEqual(Colors.Lime, Pixel(root, 200, 150), "The launch grid must expose the same sampled surface.");
            BackgroundManager.Preview(new BackgroundSettings { Kind = BackgroundKind.Image }); Pump();
            Assert.AreEqual(Colors.Lime, Pixel(root, 200, 40), "The search header must expose the shared backdrop.");
            Assert.AreEqual(Colors.Lime, Pixel(root, 200, 150), "The launch grid must not cover the same backdrop.");
            var cardFill = Assert.IsInstanceOfType<LinearGradientBrush>(card.Background,
                "Settings cards should keep the GlassDesk glass fill.");
            Assert.IsTrue(cardFill.GradientStops.All(stop => stop.Color.A < byte.MaxValue));
            BackgroundManager.Preview(new BackgroundSettings()); Pump();
            Assert.AreEqual(Colors.Lime, Pixel(root, 200, 40));
            Assert.AreEqual(Colors.Lime, Pixel(root, 200, 150));
            Assert.IsTrue(cardFill.GradientStops.All(stop => stop.Color.A < byte.MaxValue));
        }
        finally { window.Close(); BackgroundManager.EndPreview(); }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static Color Pixel(FrameworkElement element, int x, int y)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var bytes = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
        return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
}
