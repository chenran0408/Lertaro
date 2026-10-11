using System.Windows;
using System.Windows.Input;
using Lertaro.App.ViewModels.QuickPanel;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;

namespace Lertaro.App.Views.QuickPanel;

// Arrange gestures stay inside the list and never carry files to an external drop target.
internal static class QuickPanelItemReordering
{
    internal static void Register(ListBox list)
    {
        AppSearchResult? dragged = null;
        Point start = default;
        var moved = false;
        var scroll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        scroll.Tick += (_, _) =>
        {
            if (!moved || dragged == null) return;
            var viewer = Helpers.Visuals.TreeWalk.Ancestor<System.Windows.Controls.ScrollViewer>(list);
            if (viewer == null) return;
            var point = Mouse.GetPosition(viewer);
            if (point.X < 0 || point.X > viewer.ActualWidth) return;
            if (point.Y < 24) viewer.LineUp();
            else if (point.Y > viewer.ActualHeight - 24) viewer.LineDown();
        };
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (list.DataContext is not QuickPanelGroupViewModel { IsArranging: true, CanArrange: true }) return;
            e.Handled = true;
            dragged = ItemAt(e.GetPosition(list));
            if (dragged == null) return;
            start = e.GetPosition(list);
            moved = false;
            list.SelectedItem = dragged;
            list.CaptureMouse();
            scroll.Start();
        };
        list.PreviewMouseMove += (_, e) =>
        {
            if (list.DataContext is not QuickPanelGroupViewModel { IsArranging: true }) return;
            e.Handled = true;
            if (dragged == null || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(list);
            moved |= Math.Abs(point.X - start.X) > SystemParameters.MinimumHorizontalDragDistance
                || Math.Abs(point.Y - start.Y) > SystemParameters.MinimumVerticalDragDistance;
            if (moved)
            {
                list.Cursor = Cursors.SizeAll;
                list.SelectedItem = ItemAt(point) ?? dragged;
            }
        };
        list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (dragged == null) return;
            e.Handled = true;
            var point = e.GetPosition(list);
            var target = ItemAt(point);
            var item = dragged;
            dragged = null;
            scroll.Stop();
            list.ReleaseMouseCapture();
            list.ClearValue(FrameworkElement.CursorProperty);
            if (moved && list.DataContext is QuickPanelGroupViewModel { IsArranging: true } group
                && point.X >= 0 && point.X <= list.ActualWidth && point.Y >= 0)
            {
                // Blank space between tiles is not an end-of-list drop.
                if (target != null) group.MoveBefore(item.FullPath, target.FullPath);
                else if (point.Y >= list.ActualHeight - list.Margin.Bottom) group.MoveBefore(item.FullPath, null);
                list.SelectedItem = item;
            }
        };
        list.LostMouseCapture += (_, _) =>
        {
            dragged = null;
            scroll.Stop();
            list.ClearValue(FrameworkElement.CursorProperty);
        };

        AppSearchResult? ItemAt(Point point)
        {
            return list.InputHitTest(point) is not DependencyObject hit ? null : (System.Windows.Controls.ItemsControl.ContainerFromElement(list, hit) as ListBoxItem)?.DataContext as AppSearchResult;
        }
    }
}
