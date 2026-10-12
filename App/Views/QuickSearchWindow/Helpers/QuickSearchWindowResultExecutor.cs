using System.Windows;
using System.Windows.Input;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

// Executes a selected/clicked search result from the results list -- split out of QuickSearchWindow
// itself to keep that file under the line-count limit.
public class QuickSearchWindowResultExecutor
{
    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly Controls.Results.ResultClickGesture _click = new();
    private System.Windows.Point _pressPosition;

    public QuickSearchWindowResultExecutor(Lertaro.App.QuickSearchWindow window)
    {
        _window = window;
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) _click.Cancel(); };
    }

    public void HandlePreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var item = Lertaro.App.QuickSearchWindow.FindVisualParentExternal<ListBoxItem>(e.OriginalSource as DependencyObject);
        _pressPosition = e.GetPosition(_window.LstResults);
        _click.Press(item?.Content as AppSearchResult, !e.Handled && _window.MenuPresenter?.IsInActionsMode != true);
    }

    public void HandlePreviewMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(_window.LstResults);
        if (e.LeftButton != MouseButtonState.Pressed
            || Math.Abs(point.X - _pressPosition.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(point.Y - _pressPosition.Y) > SystemParameters.MinimumVerticalDragDistance) _click.Cancel();
    }

    public void HandlePreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var item = Lertaro.App.QuickSearchWindow.FindVisualParentExternal<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (!_click.Release(item?.Content as AppSearchResult, !e.Handled && _window.MenuPresenter?.IsInActionsMode != true)) return;
        if (item != null && item.Content is AppSearchResult result && !result.IsEmptyResult && !result.IsSearchSectionHeader)
        {
            e.Handled = true;
            var asAdmin = Keyboard.Modifiers == ModifierKeys.Control;
            Execute(result, asAdmin);
        }
    }

    public void HandlePreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        _click.Cancel();
        var item = Lertaro.App.QuickSearchWindow.FindVisualParentExternal<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.Content is AppSearchResult result && !result.IsEmptyResult && !result.IsSearchSectionHeader)
        {
            e.Handled = true;
            _window.LstResults.SelectedItem = result;
            _window.MenuPresenter?.EnterActionsMode(result);
        }
    }

    public void Execute(AppSearchResult result, bool asAdmin = false)
    {
        if (result.IsEmptyResult || result.IsSearchSectionHeader)
            return;
        _window.RecordKeywordHistory();
        if (!result.IsPluginSearchAction && !result.IsInstantResult)
        {
            SearchHistoryStore.Record(SearchResultHelper.HistoryKeywordOf(result, _window.TxtSearch.Text), result.FullPath, SearchResultHelper.HistoryKindOf(result));
        }

        if (result.IsPluginSearchAction)
        {
            PluginManager.Instance.TryExecuteSearchAction(result, _window, asAdmin);
            return;
        }

        if (PluginManager.Instance.TryExecuteSearchAction(result, _window, asAdmin))
        {
            return;
        }

        var currentQuery = _window.TxtSearch.Text;
        if (result.FullPath == "__SHOW_MORE__")
        {
            // The full window has no concept of a per-type trigger, so one is stripped before it lands there.
            var carriedQuery = SearchResultTypePriority.StripLeadingTrigger(currentQuery);
            _window.HideWindowNoRestore();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, carriedQuery, _window.HideWindowNoRestore);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, carriedQuery, _window.HideWindowNoRestore);
        }
        else
        {
            _window.HideWindow();
            if (asAdmin)
                FileExecutor.OpenFileOrFolderAsAdmin(result.FullPath, currentQuery, _window.HideWindow);
            else
                FileExecutor.OpenFileOrFolder(result.FullPath, currentQuery, _window.HideWindow);
        }
    }
}
