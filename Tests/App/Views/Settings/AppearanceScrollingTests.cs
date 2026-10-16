using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.App.Services.Theme;
using Lertaro.App.Views.Settings;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass, DoNotParallelize]
public sealed class AppearanceScrollingTests
{
    [StaTestMethod]
    public void RepeatedWheelInputAccumulatesAndImmediateReversalCancelsPendingMovement()
    {
        var page = new AppearanceSettingsPage(); var scroll = (ScrollViewer)page.FindName("PageScroll");
        scroll.Content = new Border { Height = 4000 };
        var window = Show(page);
        try
        {
            var step = Step(scroll);
            Wheel(scroll, -120); Wheel(scroll, -120);
            if (SystemParameters.WheelScrollLines == 0) { Assert.AreEqual(0d, scroll.VerticalOffset); return; }
            if (SystemParameters.ClientAreaAnimation && RenderCapability.Tier > 0) Assert.IsLessThan(step * 2, scroll.VerticalOffset);
            Pump(260);
            Assert.AreEqual(step * 2, scroll.VerticalOffset, .5,
                $"render tier={RenderCapability.Tier}, animation={SystemParameters.ClientAreaAnimation}");
            scroll.ScrollToVerticalOffset(0); window.UpdateLayout();
            Wheel(scroll, -120); Wheel(scroll, 120);
            Pump(260);
            Assert.AreEqual(0d, scroll.VerticalOffset, .5);
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public void NestedScrollersAndSlidersKeepTheirWheelInput()
    {
        var page = new AppearanceSettingsPage(); var scroll = (ScrollViewer)page.FindName("PageScroll");
        var slider = new Slider { Height = 30 };
        var nested = new ScrollViewer { Height = 80, Content = new Border { Height = 600 } };
        var content = new StackPanel(); content.Children.Add(slider); content.Children.Add(nested); content.Children.Add(new Border { Height = 4000 }); scroll.Content = content;
        var window = Show(page);
        try
        {
            Assert.IsGreaterThan(0d, nested.ScrollableHeight);
            Assert.IsFalse(Wheel(slider, -120).Handled);
            Assert.IsFalse(Wheel(nested, -120).Handled);
            Pump(200); Assert.AreEqual(0d, scroll.VerticalOffset);
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public void DirectInteractionAndLeavingThePageStopTheAnimation()
    {
        var page = new AppearanceSettingsPage(); var scroll = (ScrollViewer)page.FindName("PageScroll");
        scroll.Content = new Border { Height = 4000 }; var window = Show(page);
        try
        {
            Wheel(scroll, -120); Pump(30);
            scroll.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            window.UpdateLayout(); var stopped = scroll.VerticalOffset;
            Pump(220); Assert.AreEqual(stopped, scroll.VerticalOffset, .5);
            Wheel(scroll, -120); Pump(30);
            window.Content = null; window.UpdateLayout(); Pump(40);
            stopped = scroll.VerticalOffset; Pump(220);
            Assert.AreEqual(stopped, scroll.VerticalOffset, .5);
            Assert.IsFalse(page.HasAnimatedProperties);
        }
        finally { window.Close(); }
    }

    private static Window Show(AppearanceSettingsPage page)
    {
        page.Resources.MergedDictionaries.Add(new BuiltInTheme("Dark", true).GetResources());
        var window = new Window { Content = page, Width = 650, Height = 350, ShowActivated = false };
        window.Show(); window.UpdateLayout(); return window;
    }
    private static double Step(ScrollViewer scroll) => SystemParameters.WheelScrollLines < 0 ? scroll.ViewportHeight : SystemParameters.WheelScrollLines * 16d;
    private static MouseWheelEventArgs Wheel(UIElement element, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        element.RaiseEvent(args); return args;
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false; timer.Start();
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
    }
}
