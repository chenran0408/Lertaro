using System.Windows;
using System.Windows.Input;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.ViewModels.Search;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using ResultsDragDropHelper = Lertaro.App.Views.Controls.Results.ResultsDragDropHelper;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace Lertaro.App.Views.QuickSearchWindow;

public partial class QuickSearchLaunchPanel : WpfUserControl
{
    private readonly Helpers.QuickSearchLaunchShortcutSupport _shortcutSupport;

    public QuickSearchLaunchPanel()
    {
        InitializeComponent();
        _shortcutSupport = new Helpers.QuickSearchLaunchShortcutSupport(LaunchItemsListView, () => (DataContext as QuickSearchViewModel)?.LaunchPanelItems, () => (DataContext as QuickSearchViewModel)?.LaunchPanelColumns ?? 1, () => (DataContext as QuickSearchViewModel)?.LaunchPanelItemScale ?? 1);
    }

    internal void ScrollSelectedItemIntoView(AppSearchResult? item)
    {
        if (item != null)
            LaunchItemsListView.ScrollIntoView(item);
    }

    internal AppSearchResult? GetShortcutItem(Key key) => _shortcutSupport.GetItem(key);

    internal void SetActionsModeHeight(bool expanded)
    {
        if (expanded)
        {
            Height = Services.UiMetrics.ScaledQuickSearchMaxResultHeight;
            return;
        }

        ClearValue(HeightProperty);
    }

    private void Panel_PreviewDragOver(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            return;

        if (CanAcceptFileDrop(e))
        {
            e.Effects = WpfDragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void Panel_PreviewDrop(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            return;

        if (CanAcceptFileDrop(e) && e.Data.GetData(WpfDataFormats.FileDrop) is string[] paths
            && DataContext is QuickSearchViewModel viewModel)
        {
            viewModel.AddLaunchPanelDroppedPaths(paths);
        }

        e.Handled = true;
    }

    private bool CanAcceptFileDrop(WpfDragEventArgs e)
        => e.Data.GetDataPresent(WpfDataFormats.FileDrop)
            && !ResultsDragDropHelper.IsDragActive
            && DataContext is QuickSearchViewModel { CanAcceptLaunchPanelDrops: true };

    private void Panel_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return;
        if (DataContext is not QuickSearchViewModel viewModel) return;

        viewModel.CycleLaunchSource(e.Delta > 0 ? -1 : 1);
        if (LaunchSourceTabs.ItemContainerGenerator.ContainerFromItem(viewModel.SelectedLaunchSource) is FrameworkElement tab)
            tab.BringIntoView();
        e.Handled = true;
    }

    private void LaunchItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)
            || sender is not FrameworkElement { DataContext: AppSearchResult result })
            return;
        if (Window.GetWindow(this) is not Lertaro.App.QuickSearchWindow window) return;

        e.Handled = true;
        window.ExecuteFavorite(result);
    }

    private void LaunchItemMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu == null)
            return;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void EditLaunchItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuResult(sender) is { } result)
            AppWindowManager.ShowQuickLaunchItemEditor(result.FullPath);

        e.Handled = true;
    }

    private void DeleteLaunchItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is QuickSearchViewModel viewModel && GetContextMenuResult(sender) is { } result)
            viewModel.RemoveLaunchPanelItem(result);

        e.Handled = true;
    }

    private static AppSearchResult? GetContextMenuResult(object sender)
    {
        if (sender is not MenuItem menuItem || menuItem.Parent is not ContextMenu menu)
            return null;

        return (menu.PlacementTarget as FrameworkElement)?.DataContext as AppSearchResult;
    }

    private void LaunchItem_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)
            || sender is not FrameworkElement { DataContext: AppSearchResult result })
            return;

        if (Window.GetWindow(this) is not Lertaro.App.QuickSearchWindow window)
            return;

        window.EnterLaunchPanelActions(result);
        e.Handled = true;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        for (var node = source; node != null; node = TreeWalk.Parent(node))
        {
            if (node is WpfButtonBase)
                return true;
        }

        return false;
    }
}
