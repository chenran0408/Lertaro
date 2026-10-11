using System.Windows;
using System.Windows.Input;
using Lertaro.App.Views.QuickPanel;

namespace Lertaro.App.Tests.Views.QuickPanel;

[TestClass]
[DoNotParallelize]
public sealed class QuickPanelItemReorderingTests
{
    [StaTestMethod]
    public void DoubleClickOpensOnReleaseWhileDraggedOrCanceledReleaseDoesNotOpen()
    {
        var list = new System.Windows.Controls.ListBox { ItemsSource = new[] { "Entry" } };
        var window = new Window { Content = list, Width = 300, Height = 200 };
        var opened = 0;
        QuickPanelItemReordering.RegisterDoubleClick(list, () => opened++);
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Press(1); Release(false);
            Assert.AreEqual(0, opened, "A single click selects without opening.");
            Press(2);
            Assert.AreEqual(0, opened, "A second press can still become a drag.");
            Release(false);
            Assert.AreEqual(1, opened, "WPF release reports one click even after a double press.");
            Press(2); Release(true);
            Assert.AreEqual(1, opened, "A handled drag release must not open.");
            Release(false);
            Assert.AreEqual(1, opened, "A canceled or consumed double press must not remain pending.");
        }
        finally { window.Close(); }

        void Press(int clicks)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            // WPF fills this internal setter during real input; replay the observed press count.
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, clicks);
            list.RaiseEvent(args);
        }
        void Release(bool handled)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Handled = handled };
            Assert.AreEqual(1, args.ClickCount);
            list.RaiseEvent(args);
        }
    }
}
