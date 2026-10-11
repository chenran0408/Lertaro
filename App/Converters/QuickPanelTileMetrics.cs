using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Lertaro.Core;

namespace Lertaro.App.Converters;

/// <summary>One size setting controls compact launcher cells and the pictures inside them.</summary>
public sealed class QuickPanelTileMetrics : IValueConverter
{
    // Ambient size is assigned by the manager before constructing each panel window.
    public static QuickPanelThumbnailSize IconSize { get; set; } = QuickPanelThumbnailSize.ExtraLarge;
    private static double PictureSize => IconSize switch
    {
        QuickPanelThumbnailSize.Small => 32,
        QuickPanelThumbnailSize.Medium => 40,
        QuickPanelThumbnailSize.Large => 48,
        _ => 64,
    };
    internal static double MaxSlot => PictureSize + 24;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double available || !double.IsFinite(available) || available <= 0)
            return DependencyProperty.UnsetValue;
        // Picture bindings receive the measured cell width; only the default receives a list width.
        return (parameter as string) switch
        {
            "Icon" => IconWidthFor(available),
            "IconHeight" => IconHeightFor(available),
            "Cell" => CellHeightFor(available),
            _ => SlotFor(available),
        };
    }

    internal static double IconWidthFor(double slotWidth) => Math.Min(PictureSize, Math.Max(0, slotWidth - 24));
    internal static double IconHeightFor(double slotWidth) => IconWidthFor(slotWidth);
    internal static double CellHeightFor(double slotWidth) => IconHeightFor(slotWidth) + 30;
    internal static double SlotFor(double available) => Math.Max(0, Math.Min(MaxSlot, available));
    internal static int ColumnsFor(double available)
        => !double.IsFinite(available) || available <= 0 ? 1 : Math.Max(1, (int)Math.Floor(available / MaxSlot));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
