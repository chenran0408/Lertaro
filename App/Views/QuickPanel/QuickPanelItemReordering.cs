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

// Reordering gestures stay inside the panel. A separate transparent popup avoids item clipping while moving.
internal static class QuickPanelItemReordering
{
    private static readonly DependencyProperty CancelProperty = DependencyProperty.RegisterAttached(
        "Cancel", typeof(Action), typeof(QuickPanelItemReordering));

    internal static void Cancel(DependencyObject root)
    {
        if (root.GetValue(CancelProperty) is Action cancel) cancel();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            Cancel(VisualTreeHelper.GetChild(root, index));
    }

    internal static void Register(ListBox list)
    {
        AppSearchResult? dragged = null;
        Point start = default;
        Vector offset = default;
        var moved = false;
        var suppressRelease = false;
        System.Windows.Controls.Primitives.Popup? ghost = null;
        QuickPanelNumberedWrapPanel? grid = null;
        var scroll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        list.SetValue(CancelProperty, (Action)Cancel);
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
            Cancel();
            suppressRelease = false;
            if (Keyboard.Modifiers != ModifierKeys.None
                || list.DataContext is not QuickPanelGroupViewModel { CanArrange: true }) return;
            dragged = ItemAt(e.GetPosition(list));
            if (dragged == null) return;
            start = e.GetPosition(list);
            moved = false;
        };
        list.PreviewMouseMove += (_, e) =>
        {
            if (dragged == null) return;
            if (e.LeftButton != MouseButtonState.Pressed
                || list.DataContext is not QuickPanelGroupViewModel { CanArrange: true } group
                || (moved && !group.IsReordering))
            {
                Cancel();
                return;
            }
            e.Handled = true;
            var point = e.GetPosition(list);
            if (!moved)
            {
                if (Math.Abs(point.X - start.X) <= SystemParameters.MinimumHorizontalDragDistance
                    && Math.Abs(point.Y - start.Y) <= SystemParameters.MinimumVerticalDragDistance) return;
                group.IsReordering = true;
                if (!list.SelectedItems.Contains(dragged)) list.SelectedItem = dragged;
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
                moved = true;
                scroll.Start();
            }
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
            if (dragged == null)
            {
                if (suppressRelease) { e.Handled = true; suppressRelease = false; }
                return;
            }
            var item = dragged;
            var point = e.GetPosition(list);
            var target = ItemAt(point);
            var slot = grid?.SlotAt(e.GetPosition(grid)) ?? -1;
            var originalSlot = list.DataContext is QuickPanelGroupViewModel { UsesGridSlots: true } current
                && current.GridSlots.TryGetValue(item.FullPath, out var savedSlot) ? savedSlot : list.Items.IndexOf(item);
            var wasMoved = moved && list.DataContext is QuickPanelGroupViewModel { IsReordering: true };
            var viewport = Helpers.Visuals.TreeWalk.Ancestor<System.Windows.Controls.ScrollViewer>(list);
            var insideViewport = viewport == null || new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight).Contains(e.GetPosition(viewport));
            var selected = list.SelectedItems.OfType<AppSearchResult>().Select(result => result.FullPath).ToArray();
            Cancel();
            if (!wasMoved) return;
            e.Handled = true;
            if (insideViewport && list.DataContext is QuickPanelGroupViewModel { CanArrange: true } group)
            {
                if (slot >= 0 && point.Y < list.ActualHeight && (selected.Length > 1 || slot != originalSlot)) group.PlaceInGrid(selected.Length > 0 ? selected : new[] { item.FullPath }, slot);
                else if (grid == null && target != null) group.MoveTo(item.FullPath, target.FullPath);
            }
        };
        list.LostMouseCapture += (_, _) => { if (moved && Mouse.Captured != list) Cancel(); };
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
            var ownedCapture = moved;
            suppressRelease |= ownedCapture;
            ResetVisual();
            dragged = null;
            moved = false;
            if (list.DataContext is QuickPanelGroupViewModel group) group.IsReordering = false;
            if (ownedCapture) Controls.Results.ResultsDragDropHelper.CancelPendingDrag(list);
            if (ownedCapture && Mouse.Captured == list) list.ReleaseMouseCapture();
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
