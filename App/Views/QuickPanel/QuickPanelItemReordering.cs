using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.ViewModels.QuickPanel;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;

namespace Lertaro.App.Views.QuickPanel;

// Arrange gestures stay inside the panel. A separate transparent popup avoids item clipping while moving.
internal static class QuickPanelItemReordering
{
    internal static void Register(ListBox list)
    {
        AppSearchResult? dragged = null;
        Point start = default;
        Vector offset = default;
        var moved = false;
        System.Windows.Controls.Primitives.Popup? ghost = null;
        QuickPanelNumberedWrapPanel? grid = null;
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
            grid = QuickPanelNumberedWrapPanel.Find(list);
            if (list.ItemContainerGenerator.ContainerFromItem(dragged) is ListBoxItem container)
            {
                offset = start - container.TranslatePoint(new Point(), list);
                var dpi = VisualTreeHelper.GetDpi(container);
                var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(container.ActualWidth * dpi.DpiScaleX)),
                    Math.Max(1, (int)Math.Ceiling(container.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen())
                    context.DrawRectangle(new VisualBrush(container), null, new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                bitmap.Render(drawing);
                bitmap.Freeze();
                ghost = new System.Windows.Controls.Primitives.Popup
                {
                    PlacementTarget = list, Placement = System.Windows.Controls.Primitives.PlacementMode.Relative,
                    AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false,
                    Child = new System.Windows.Controls.Image { Source = bitmap, Width = container.ActualWidth, Height = container.ActualHeight, Opacity = 0.85, IsHitTestVisible = false }
                };
            }
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
            if (!moved) return;
            list.Cursor = Cursors.SizeAll;
            if (ghost != null)
            {
                ghost.HorizontalOffset = point.X - offset.X;
                ghost.VerticalOffset = point.Y - offset.Y;
                ghost.IsOpen = true;
            }
            grid?.TargetSlot = grid.SlotAt(e.GetPosition(grid));
        };
        list.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (dragged == null) return;
            e.Handled = true;
            var point = e.GetPosition(list);
            var target = ItemAt(point);
            var slot = grid?.SlotAt(e.GetPosition(grid)) ?? -1;
            var item = dragged;
            ResetVisual();
            dragged = null;
            scroll.Stop();
            list.ReleaseMouseCapture();
            list.ClearValue(FrameworkElement.CursorProperty);
            if (moved && list.DataContext is QuickPanelGroupViewModel { IsArranging: true } group)
            {
                if (slot >= 0) group.PlaceInGrid(new[] { item.FullPath }, slot);
                else if (grid == null && target != null) group.MoveTo(item.FullPath, target.FullPath);
                else if (grid == null && point.X >= 0 && point.X <= list.ActualWidth && point.Y >= list.ActualHeight)
                    group.MoveTo(item.FullPath, null);
                list.SelectedItem = item;
            }
        };
        list.LostMouseCapture += (_, _) => Cancel();
        list.Unloaded += (_, _) => Cancel();
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || dragged == null) return;
            e.Handled = true;
            Cancel();
            list.ReleaseMouseCapture();
        };

        void Cancel()
        {
            ResetVisual();
            dragged = null;
            scroll.Stop();
            list.ClearValue(FrameworkElement.CursorProperty);
        }
        void ResetVisual()
        {
            ghost?.IsOpen = false;
            ghost = null;
            grid?.TargetSlot = -1;
        }
        AppSearchResult? ItemAt(Point point)
            => list.InputHitTest(point) is not DependencyObject hit ? null
                : (System.Windows.Controls.ItemsControl.ContainerFromElement(list, hit) as ListBoxItem)?.DataContext as AppSearchResult;
    }
}
