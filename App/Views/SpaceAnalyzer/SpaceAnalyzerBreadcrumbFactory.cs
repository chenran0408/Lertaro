using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Services;

namespace Lertaro.App.Views.SpaceAnalyzer;

/// <summary>
/// Split out solely to keep the analyzer view under the repository's per-file line limit; it creates
/// the stateless breadcrumb separator used between location buttons.
/// </summary>
internal static class SpaceAnalyzerBreadcrumbFactory
{
    public static TextBlock Create()
    {
        var separator = new TextBlock
        {
            Text = "\uE76C",
            FontFamily = AppTypography.IconFont,
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0)
        };
        separator.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        return separator;
    }
}
