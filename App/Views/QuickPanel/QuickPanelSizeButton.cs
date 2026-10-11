using System.Windows;
using System.Windows.Media;
using Lertaro.App.Converters;
using Lertaro.App.Services;
using Lertaro.Core;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Lertaro.App.Views.QuickPanel;

// A local size picker keeps changes on the current panel without rebuilding its groups or window.
internal sealed class QuickPanelSizeButton : Button
{
    public QuickPanelSizeButton()
    {
        Content = "↔";
        FontSize = 18;
        Width = Height = 32;
        Background = System.Windows.Media.Brushes.Transparent;
        BorderThickness = new Thickness(0);
        SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[QuickPanel_ThumbnailSize]") { Source = TranslationManager.Instance });
    }

    protected override void OnClick()
    {
        base.OnClick();
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var size in Enum.GetValues<QuickPanelThumbnailSize>())
        {
            var item = new MenuItem { Header = TranslationManager.Instance["QuickPanel_ThumbnailSize" + size], IsCheckable = true, IsChecked = QuickPanelTileMetrics.IconSize == size };
            item.Click += (_, _) =>
            {
                var settings = UserSettings.Load();
                settings.QuickPanel.ThumbnailIconSize = size;
                settings.Save();
                if (Window.GetWindow(this) is { } window) ApplySize(window, size);
            };
            menu.Items.Add(item);
        }
        ContextMenu = menu;
        menu.IsOpen = true;
    }

    internal static void ApplySize(DependencyObject root, QuickPanelThumbnailSize size)
    {
        QuickPanelTileMetrics.IconSize = size;
        Refresh(root);
    }

    private static void Refresh(DependencyObject root)
    {
        if (root is FrameworkElement element)
        {
            if (element.Name == "TileIcon")
            {
                element.GetBindingExpression(WidthProperty)?.UpdateTarget();
                element.GetBindingExpression(HeightProperty)?.UpdateTarget();
            }
            if (element is QuickPanelNumberedWrapPanel) { element.InvalidateMeasure(); element.InvalidateVisual(); }
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Refresh(VisualTreeHelper.GetChild(root, i));
    }
}
