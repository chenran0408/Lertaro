using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Lertaro.App.Converters;

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
    public static readonly DependencyProperty ShowGridLinesProperty = DependencyProperty.Register(
        nameof(ShowGridLines), typeof(bool), typeof(QuickPanelNumberedWrapPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool ShowGridLines { get => (bool)GetValue(ShowGridLinesProperty); set => SetValue(ShowGridLinesProperty, value); }
    public IReadOnlyDictionary<string, int>? GridSlots { get => (IReadOnlyDictionary<string, int>?)GetValue(GridSlotsProperty); set => SetValue(GridSlotsProperty, value); }
    public bool UsesGridSlots { get => (bool)GetValue(UsesGridSlotsProperty); set => SetValue(UsesGridSlotsProperty, value); }
    private int _targetSlot = -1;
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
        if (UsesGridSlots && (ShowGridLines || _targetSlot >= 0))
        {
            var outline = new System.Windows.Media.Pen(new SolidColorBrush(System.Windows.Media.Color.FromArgb(35, 128, 128, 128)), 1);
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < _columns; column++)
                    context.DrawRoundedRectangle(null, outline, new Rect(_gutterWidth + column * _slotWidth + 2, row * height + 2, Math.Max(0, _slotWidth - 4), Math.Max(0, height - 4)), 5, 5);
            if (_targetSlot >= 0)
                context.DrawRoundedRectangle(new SolidColorBrush(System.Windows.Media.Color.FromArgb(65, 60, 140, 230)), outline,
                    new Rect(_gutterWidth + _targetSlot % _columns * _slotWidth, _targetSlot / _columns * height, _slotWidth, height), 5, 5);
        }
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

    internal static QuickPanelNumberedWrapPanel? Find(DependencyObject root)
    {
        if (root is QuickPanelNumberedWrapPanel panel) return panel;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private int SlotFor(int index)
        => UsesGridSlots && InternalChildren[index] is FrameworkElement { DataContext: AppSearchResult item }
            && GridSlots?.TryGetValue(item.FullPath, out var slot) == true ? slot : index;

    private int RowCount()
    {
        var count = UsesGridSlots && InternalChildren.Count > 0 ? Enumerable.Range(0, InternalChildren.Count).Max(SlotFor) + 1 : InternalChildren.Count;
        var rows = QuickPanelLineNumberLayout.RowsFor(count, Math.Max(1, _columns));
        // Keep a free row available for placing icons without squeezing existing cells together.
        return UsesGridSlots ? rows + 1 : Math.Max(1, rows);
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
