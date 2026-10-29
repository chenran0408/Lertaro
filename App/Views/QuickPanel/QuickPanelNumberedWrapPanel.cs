using System.Globalization;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using Lertaro.App.Converters;
using Point = System.Windows.Point;

namespace Lertaro.App.Views.QuickPanel;

/// <summary>Arranges thumbnail tiles after one gutter shared with every visible quick-panel group.</summary>
internal sealed class QuickPanelNumberedWrapPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty MaximumItemCountProperty = DependencyProperty.Register(
        nameof(MaximumItemCount), typeof(int), typeof(QuickPanelNumberedWrapPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridSlotsProperty = DependencyProperty.Register(
        nameof(GridSlots), typeof(IReadOnlyDictionary<string, int>), typeof(QuickPanelNumberedWrapPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UsesGridSlotsProperty = DependencyProperty.Register(
        nameof(UsesGridSlots), typeof(bool), typeof(QuickPanelNumberedWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ReserveDropRowProperty = DependencyProperty.Register(
        nameof(ReserveDropRow), typeof(bool), typeof(QuickPanelNumberedWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ReserveDropRow { get => (bool)GetValue(ReserveDropRowProperty); set => SetValue(ReserveDropRowProperty, value); }
    public IReadOnlyDictionary<string, int>? GridSlots { get => (IReadOnlyDictionary<string, int>?)GetValue(GridSlotsProperty); set => SetValue(GridSlotsProperty, value); }
    public bool UsesGridSlots { get => (bool)GetValue(UsesGridSlotsProperty); set => SetValue(UsesGridSlotsProperty, value); }
    private int _targetSlot = -1;
    private IReadOnlyDictionary<string, int>? _previewGridSlots;
    private readonly Dictionary<UIElement, TranslateTransform> _previewMotions = new();
    internal int TargetSlot { get => _targetSlot; set { _targetSlot = value; InvalidateVisual(); } }
    private double _gutterWidth;
    private double _slotWidth;
    private int _columns;

    public int MaximumItemCount { get => (int)GetValue(MaximumItemCountProperty); set => SetValue(MaximumItemCountProperty, value); }

    internal int Columns => _columns;

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        CalculateLayout(availableSize.Width);
        var height = RowCount() * CellHeight();
        foreach (UIElement child in InternalChildren)
            child.Measure(new System.Windows.Size(_slotWidth, CellHeight()));
        return new System.Windows.Size(availableSize.Width, height);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        CalculateLayout(finalSize.Width);
        var height = CellHeight();
        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var slot = SlotFor(index);
            InternalChildren[index].Arrange(new Rect(
                _gutterWidth + slot % _columns * _slotWidth, slot / _columns * height, _slotWidth, height));
        }
        return finalSize;
    }

    protected override void OnRender(DrawingContext context)
    {
        var digits = QuickPanelLineNumberLayout.DigitsFor(QuickPanelLineNumberLayout.RowsFor(MaximumItemCount, _columns));
        var rows = RowCount();
        var height = CellHeight();
        var iconHeight = QuickPanelTileMetrics.IconHeightFor(_slotWidth);
        for (var row = 0; row < rows; row++)
        {
            var text = NumberText(row + 1, digits);
            context.DrawText(text, new System.Windows.Point(_gutterWidth - text.Width - 8, row * height + (iconHeight - text.Height) / 2));
        }
    }

    internal int SlotAt(System.Windows.Point point)
    {
        if (_columns <= 0 || _slotWidth <= 0 || point.X < _gutterWidth || point.X >= _gutterWidth + _columns * _slotWidth || point.Y < 0 || point.Y >= ActualHeight) return -1;
        return (int)(point.Y / CellHeight()) * _columns + Math.Min(_columns - 1, (int)((point.X - _gutterWidth) / _slotWidth));
    }

    internal void PreviewPlacement(IReadOnlyDictionary<string, int>? slots)
    {
        var current = InternalChildren.Cast<UIElement>()
            .ToDictionary(child => child, child => child.TranslatePoint(new Point(), this));
        foreach (var child in _previewMotions.Keys.Where(child => !current.ContainsKey(child)).ToArray())
            _previewMotions.Remove(child);
        foreach (var (child, motion) in _previewMotions)
        {
            motion.BeginAnimation(TranslateTransform.XProperty, null);
            motion.BeginAnimation(TranslateTransform.YProperty, null);
            motion.X = motion.Y = 0;
        }

        _previewGridSlots = slots;
        InvalidateMeasure();
        UpdateLayout();
        var duration = TimeSpan.FromMilliseconds(210);
        foreach (var child in InternalChildren.Cast<UIElement>())
        {
            if (!current.TryGetValue(child, out var previous)) continue;
            var target = child.TranslatePoint(new Point(), this);
            var x = previous.X - target.X;
            var y = previous.Y - target.Y;
            if (Math.Abs(x) < .5 && Math.Abs(y) < .5) continue;

            var motion = MotionFor(child);
            if (Math.Abs(x) >= .5)
                motion.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(x, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            if (Math.Abs(y) >= .5)
                motion.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(y, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
    }

    internal static QuickPanelNumberedWrapPanel? Find(DependencyObject root)
    {
        if (root is QuickPanelNumberedWrapPanel panel) return panel;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private int SlotFor(int index)
    {
        if (InternalChildren[index] is not FrameworkElement { DataContext: AppSearchResult item }) return index;
        if (_previewGridSlots?.TryGetValue(item.FullPath, out var preview) == true) return preview;
        return UsesGridSlots && GridSlots?.TryGetValue(item.FullPath, out var slot) == true ? slot : index;
    }

    private int RowCount()
    {
        var count = UsesGridSlots && InternalChildren.Count > 0 ? Enumerable.Range(0, InternalChildren.Count).Max(SlotFor) + 1 : InternalChildren.Count;
        var rows = QuickPanelLineNumberLayout.RowsFor(count, Math.Max(1, _columns));
        // Keep a free row available for placing icons without squeezing existing cells together.
        return UsesGridSlots || ReserveDropRow ? rows + 1 : Math.Max(1, rows);
    }

    private TranslateTransform MotionFor(UIElement child)
    {
        if (_previewMotions.TryGetValue(child, out var existing)) return existing;
        var motion = new TranslateTransform();
        var current = child.RenderTransform;
        if (current == Transform.Identity) child.RenderTransform = motion;
        else
        {
            var group = new TransformGroup();
            group.Children.Add(current);
            group.Children.Add(motion);
            child.RenderTransform = group;
        }
        _previewMotions[child] = motion;
        return motion;
    }

    private void CalculateLayout(double width)
    {
        _gutterWidth = GutterWidth(QuickPanelLineNumberLayout.RowsFor(MaximumItemCount, 1));
        for (var pass = 0; pass < 2; pass++)
        {
            _columns = QuickPanelLineNumberLayout.ThumbnailColumnsFor(width, _gutterWidth);
            _gutterWidth = GutterWidth(QuickPanelLineNumberLayout.RowsFor(MaximumItemCount, _columns));
        }
        _slotWidth = QuickPanelTileMetrics.SlotFor(Math.Max(0, width - _gutterWidth));
    }

    private double CellHeight() => QuickPanelTileMetrics.CellHeightFor(_slotWidth);
    private double GutterWidth(int maximumRowCount) => NumberText(maximumRowCount, QuickPanelLineNumberLayout.DigitsFor(maximumRowCount)).Width + 8;
    private FormattedText NumberText(int number, int digits) => new(
        number.ToString($"D{digits}", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
        System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, System.Windows.Media.Brushes.Gray,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
