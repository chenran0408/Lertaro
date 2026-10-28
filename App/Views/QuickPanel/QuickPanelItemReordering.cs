using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Lertaro.App.ViewModels.QuickPanel;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;

namespace Lertaro.App.Views.QuickPanel;

// Reordering gestures stay inside the panel and follow the source tile in its drag overlay.
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

    internal static void RegisterSingleClick(ListBox list, Action open)
    {
        // Open on release, after the drag handler has had a chance to claim the gesture.
        ListBoxItem? pressed = null;
        list.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((_, args) =>
            pressed = args.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None
                ? System.Windows.Controls.ItemsControl.ContainerFromElement(list, args.OriginalSource as DependencyObject) as ListBoxItem
                : null), true);
        list.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler((_, args) =>
        {
            var item = pressed;
            pressed = null;
            if (args.Handled || item == null || Keyboard.Modifiers != ModifierKeys.None) return;
            if (System.Windows.Controls.ItemsControl.ContainerFromElement(list, args.OriginalSource as DependencyObject) != item) return;
            list.SelectedItem = item.DataContext;
            open();
        }), true);
    }

    internal static void Register(ListBox list)
    {
        AppSearchResult? dragged = null;
        Point start = default;
        Vector offset = default;
        var moved = false;
        var suppressRelease = false;
        System.Windows.Controls.Image? ghost = null;
        Canvas? overlay = null;
        TranslateTransform? ghostMotion = null;
        ListBoxItem? ghostSource = null;
        QuickPanelNumberedWrapPanel? grid = null;
        Action? finishDropAnimation = null;
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
                    overlay = Window.GetWindow(list)?.FindName("DragOverlay") as Canvas;
                    if (overlay != null)
                    {
                        ghost = new System.Windows.Controls.Image
                        {
                            Source = bitmap,
                            Width = container.ActualWidth,
                            Height = container.ActualHeight,
                            IsHitTestVisible = false,
                            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 4, Opacity = .22, Color = Colors.Black }
                        };
                        var transforms = new TransformGroup();
                        transforms.Children.Add(new ScaleTransform(1.06, 1.06));
                        transforms.Children.Add(ghostMotion = new TranslateTransform());
                        ghost.RenderTransform = transforms;
                        overlay.Children.Add(ghost);
                        var position = container.TranslatePoint(new Point(), overlay);
                        Canvas.SetLeft(ghost, position.X);
                        Canvas.SetTop(ghost, position.Y);
                        ghostSource = container;
                        container.Opacity = 0;
                    }
                }
                list.CaptureMouse();
                moved = true;
                scroll.Start();
            }
            list.Cursor = Cursors.SizeAll;
            if (grid != null)
            {
                var slot = grid.SlotAt(e.GetPosition(grid));
                if (grid.TargetSlot != slot)
                {
                    grid.TargetSlot = slot;
                    grid.PreviewPlacement(slot >= 0 && list.DataContext is QuickPanelGroupViewModel previewGroup
                        ? PreviewGridSlots(previewGroup, list, slot)
                        : null);
                }
            }
            if (ghost != null && overlay != null && ghostMotion != null)
            {
                var pointer = Mouse.GetPosition(overlay);
                ghostMotion.X = pointer.X - offset.X - Canvas.GetLeft(ghost);
                ghostMotion.Y = pointer.Y - offset.Y - Canvas.GetTop(ghost);
            }
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
            var wasMoved = moved && list.DataContext is QuickPanelGroupViewModel { IsReordering: true };
            var viewport = Helpers.Visuals.TreeWalk.Ancestor<System.Windows.Controls.ScrollViewer>(list);
            var insideViewport = viewport == null || new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight).Contains(e.GetPosition(viewport));
            var selected = list.SelectedItems.OfType<AppSearchResult>().Select(result => result.FullPath).ToArray();
            var group = list.DataContext as QuickPanelGroupViewModel;
            var placeInGrid = wasMoved && insideViewport && grid != null && slot >= 0 && group is { CanArrange: true };
            if (!wasMoved) { Cancel(); return; }
            e.Handled = true;
            if (placeInGrid && group != null)
            {
                group.PlaceInGrid(selected.Length > 1 ? selected : new[] { item.FullPath }, slot);
                grid!.PreviewPlacement(group.GridSlots);
                list.UpdateLayout();
                var landing = list.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                if (landing != null && ghost != null && ghostMotion != null && overlay != null)
                {
                    landing.Opacity = 0;
                    landing.IsHitTestVisible = false;
                    EndDrag(keepGhost: true);
                    AnimateGhostToLanding(landing);
                    return;
                }
            }
            else if (insideViewport && group is { CanArrange: true } && grid == null && target != null)
                group.MoveTo(item.FullPath, target.FullPath);
            Cancel();
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

        void Cancel() => EndDrag(keepGhost: false);

        void EndDrag(bool keepGhost)
        {
            var ownedCapture = moved;
            suppressRelease |= ownedCapture;
            finishDropAnimation?.Invoke();
            finishDropAnimation = null;
            if (!keepGhost) ResetVisual();
            else
            {
                grid?.TargetSlot = -1;
                grid?.PreviewPlacement(null);
            }
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
            if (ghostSource != null) ghostSource.Opacity = 1;
            if (ghost != null && overlay != null) overlay.Children.Remove(ghost);
            ghost = null;
            ghostMotion = null;
            ghostSource = null;
            grid?.TargetSlot = -1;
            grid?.PreviewPlacement(null);
        }

        void AnimateGhostToLanding(ListBoxItem landing)
        {
            if (ghost == null || ghostMotion == null || overlay == null) { Cancel(); return; }
            var currentGhost = ghost;
            var currentMotion = ghostMotion;
            var destination = landing.TranslatePoint(new Point(), overlay);
            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var cleanup = () =>
            {
                landing.Opacity = 1;
                landing.IsHitTestVisible = true;
                currentMotion.BeginAnimation(TranslateTransform.XProperty, null);
                currentMotion.BeginAnimation(TranslateTransform.YProperty, null);
                if (overlay.Children.Contains(currentGhost)) overlay.Children.Remove(currentGhost);
                if (ReferenceEquals(ghost, currentGhost)) { ghost = null; ghostMotion = null; ghostSource = null; }
            };
            finishDropAnimation = cleanup;
            var x = new DoubleAnimation(currentMotion.X, destination.X - Canvas.GetLeft(currentGhost), duration) { EasingFunction = ease };
            var y = new DoubleAnimation(currentMotion.Y, destination.Y - Canvas.GetTop(currentGhost), duration) { EasingFunction = ease };
            y.Completed += (_, _) =>
            {
                if (ReferenceEquals(finishDropAnimation, cleanup)) finishDropAnimation = null;
                cleanup();
            };
            currentMotion.BeginAnimation(TranslateTransform.XProperty, x);
            currentMotion.BeginAnimation(TranslateTransform.YProperty, y);
            if (currentGhost.RenderTransform is TransformGroup transforms && transforms.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.06, 1, duration) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.06, 1, duration) { EasingFunction = ease });
            }
        }

        static IReadOnlyDictionary<string, int> PreviewGridSlots(QuickPanelGroupViewModel group, ListBox source, int targetSlot)
        {
            var slots = group.UsesGridSlots
                ? group.GridSlots
                : source.Items.OfType<AppSearchResult>().Select((result, index) => (result.FullPath, index))
                    .ToDictionary(pair => pair.FullPath, pair => pair.index, StringComparer.OrdinalIgnoreCase);
            var selected = source.SelectedItems.OfType<AppSearchResult>().Select(result => result.FullPath).ToArray();
            return QuickPanelGridLayout.Place(slots, selected, targetSlot);
        }
        AppSearchResult? ItemAt(Point point)
            => list.InputHitTest(point) is not DependencyObject hit ? null
                : (System.Windows.Controls.ItemsControl.ContainerFromElement(list, hit) as ListBoxItem)?.DataContext as AppSearchResult;
    }
}
