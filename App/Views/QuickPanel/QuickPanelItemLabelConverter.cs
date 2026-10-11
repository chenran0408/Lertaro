using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickPanel;

internal sealed class QuickPanelItemLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var original = values.ElementAtOrDefault(0) as string ?? string.Empty;
        var path = values.ElementAtOrDefault(1) as string ?? string.Empty;
        var labels = values.ElementAtOrDefault(2) as IReadOnlyDictionary<string, QuickPanelItemLabel>;
        QuickPanelItemLabel? label = null;
        labels?.TryGetValue(path, out label);
        return parameter as string == "Visibility" ? label?.HideName == true ? Visibility.Collapsed : Visibility.Visible
            : string.IsNullOrEmpty(label?.DisplayName) ? original : label.DisplayName;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
