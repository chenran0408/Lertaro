using System.Windows;
using System.Windows.Input;
using Lertaro.App.Views.QuickPanel;

namespace Lertaro.App.Tests.Views.QuickPanel;

[TestClass]
[DoNotParallelize]
public sealed class QuickPanelItemReorderingTests
{
    [StaTestMethod]
    public void SingleClickOpensOnceWhileDragAndBackgroundDoNotOpen()
    {
        var list = new System.Windows.Controls.ListBox { ItemsSource = new[] { "Entry", "Other" } };
        var window = new Window { Content = list, Width = 300, Height = 200 };
        var opened = 0;
        QuickPanelItemReordering.RegisterSingleClick(list, () => opened++);
        try
        {
            window.Show();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var first = (System.Windows.Controls.ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)!;
            var second = (System.Windows.Controls.ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1)!;
            Press(first, 1); Release(first, false);
            Assert.AreEqual(1, opened, "The first click opens on release.");
            Assert.AreEqual("Entry", list.SelectedItem);
            Press(first, 2); Release(first, false);
            Assert.AreEqual(1, opened, "The second half of a double-click must not launch twice.");
            Press(first, 1); Release(first, true);
            Assert.AreEqual(1, opened, "A handled drag release must not open.");
            Press(first, 1); Release(second, false);
            Assert.AreEqual(1, opened, "Releasing over another item must not open it.");
            Press(first, 1); Release(list, false);
            Assert.AreEqual(1, opened, "Releasing over blank space must not open the old selection.");
        }
        finally { window.Close(); }

        void Press(UIElement target, int clicks)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            // WPF fills this internal setter during real input; replay the observed press count.
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(args, clicks);
            target.RaiseEvent(args);
        }
        void Release(UIElement target, bool handled)
        {
            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Handled = handled };
            Assert.AreEqual(1, args.ClickCount);
            target.RaiseEvent(args);
        }
    }
}
