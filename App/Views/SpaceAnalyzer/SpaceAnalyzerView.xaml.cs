using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.App.Services.ShellIcons;
using Lertaro.App.Services.Theme;
using Lertaro.App.ViewModels.SpaceAnalyzer;
using Lertaro.Core.Services.Search;
using Lertaro.PluginSdk.Abstractions;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;
namespace Lertaro.App.Views.SpaceAnalyzer;
public partial class SpaceAnalyzerView : UserControl, IDisposable
{
    private readonly List<SpaceAnalyzerLocation> _history = [];
    private readonly SearchService _searchService = new();
    private readonly SpaceAnalyzerRefreshWatcher _refreshWatcher;
    private readonly SpaceAnalyzerPreviewSupport _previewSupport;
    private IReadOnlyList<SpaceDisplayItem> _items = Array.Empty<SpaceDisplayItem>();
    private IReadOnlyList<SpaceDisplayItem> _visibleItems = Array.Empty<SpaceDisplayItem>();
    private bool? _showTreemap;
    private CancellationTokenSource? _loadCts;
    private bool _isLoading;
    private bool _loadFailed;
    private bool _initialized;
    private bool _disposed;
    public SpaceAnalyzerView()
    {
        InitializeComponent();
        _refreshWatcher = new SpaceAnalyzerRefreshWatcher(Dispatcher, ReloadFromEventAsync, ValidateLocationFromEventAsync);
        _previewSupport = new SpaceAnalyzerPreviewSupport(this, () => (ItemsList.SelectedItem as SpaceDisplayItem)?.Path);
        SpaceAnalyzerMiddleClick.Attach(ItemsList);
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += (_, _) => Dispose();
    }
    private async void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed)
            return;
        if (!IsVisible)
        {
            _loadCts?.Cancel();
            _refreshWatcher.Pause();
            _previewSupport.SetVisible(false);
            ActionFlyout.Close();
            return;
        }
        _refreshWatcher.AttachDeviceNotifications(this);
        if (!_initialized)
        {
            _initialized = true;
            _history.Add(new SpaceAnalyzerLocation(null, TranslationManager.Instance["Space_Home"]));
            _refreshWatcher.Watch(_history.Select(location => location.Path).ToList());
            await ReloadAsync();
            return;
        }
        ItemsList.SelectedItem = null;
        if (_history.Count > 1)
            _history.RemoveRange(1, _history.Count - 1);
        LocationFilter.Clear();
        _refreshWatcher.Watch(_history.Select(location => location.Path).ToList());
        _previewSupport.SetVisible(true);
        await ReloadAsync();
    }
    private Task ReloadFromEventAsync()
    {
        if (!IsVisible) return Task.CompletedTask;
        if (!_isLoading) return ReloadAsync(background: true);
        _refreshWatcher.RequestRefresh();
        return Task.CompletedTask;
    }
    private Task ValidateLocationFromEventAsync() => _isLoading || !IsVisible ? Task.CompletedTask : ReloadAsync(true, true);
    private async Task ReloadAsync(bool background = false, bool onlyIfLocationUnavailable = false)
    {
        var selectedPath = background ? (ItemsList.SelectedItem as SpaceDisplayItem)?.Path : null;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        if (!background)
        {
            _loadFailed = false;
            RebuildBreadcrumbs();
            SetLoading(true);
        }
        try
        {
            var locationChanged = background && await SpaceAnalyzerLocationResolver.TrimUnavailableAsync(
                _history, async (path, tok) => (await SpaceBrowserLoader.LoadAsync(_searchService, path, tok)).Select(item => item.Entry).ToList(), token);
            if (onlyIfLocationUnavailable && !locationChanged)
                return;
            var items = await SpaceBrowserLoader.LoadAsync(_searchService, _history[^1].Path, token);
            token.ThrowIfCancellationRequested();
            _items = items;
            ShowCurrentLocation(selectedPath);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[SpaceAnalyzer] Failed to query live indexes: {ex.Message}", Core.LogLevel.Error);
            if (background)
                return;
            _loadFailed = true;
            _items = Array.Empty<SpaceDisplayItem>();
            ShowCurrentLocation();
        }
        finally
        {
            if (!background && !token.IsCancellationRequested)
                SetLoading(false);
        }
    }
    private void ShowCurrentLocation(string? selectedPath = null)
    {
        if (_history.Count == 0)
            return;
        ParentListButton.Visibility = _history.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        RebuildBreadcrumbs();
        ApplyLocationFilter(selectedPath);
        if (IsVisible)
            _refreshWatcher.Watch(_history.Select(location => location.Path).ToList());
    }

    private void ApplyLocationFilter(string? selectedPath)
    {
        var filter = LocationFilter.Text.Trim();
        _visibleItems = filter.Length == 0 ? _items
            : _items.Where(item => item.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        ItemsList.ItemsSource = _visibleItems;
        ItemsList.SelectedItem = selectedPath == null
            ? null
            : _visibleItems.FirstOrDefault(item => string.Equals(item.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        UpdateEmptyState();
        UpdateSummary();
        RenderTreemap();
    }
    private async void ActivateItem(SpaceDisplayItem item)
    {
        if (item.OpenInFileManager)
        {
            FileExecutor.OpenFileOrFolder(item.Path);
            return;
        }
        if (!item.IsDirectory)
        {
            FileExecutor.LocateInExplorer(item.Path);
            return;
        }
        ItemsList.SelectedItem = null;
        _history.Add(new SpaceAnalyzerLocation(item.Path, item.Name));
        LocationFilter.Clear();
        await ReloadAsync();
    }
    private async void NavigateToHistory(int index)
    {
        if ((uint)index >= (uint)_history.Count)
            return;
        ItemsList.SelectedItem = null;
        _history.RemoveRange(index + 1, _history.Count - index - 1);
        LocationFilter.Clear();
        await ReloadAsync();
    }

    private void RebuildBreadcrumbs()
    {
        BreadcrumbPanel.Children.Clear();
        for (var index = 0; index < _history.Count; index++)
        {
            if (index > 0)
                BreadcrumbPanel.Children.Add(SpaceAnalyzerBreadcrumbFactory.Create());
            var target = index;
            var isCurrent = index == _history.Count - 1;
            var button = new Button
            {
                Content = _history[index].Name,
                Style = (Style)FindResource(isCurrent ? "SpaceCurrentBreadcrumbButton" : "SpaceBreadcrumbButton"),
                IsHitTestVisible = !isCurrent
            };
            if (!isCurrent)
                button.Click += (_, _) => NavigateToHistory(target);
            BreadcrumbPanel.Children.Add(button);
        }
        BreadcrumbScroller.ScrollToRightEnd();
    }

    private void RenderTreemap()
    {
        if (MapPane.Visibility == Visibility.Visible)
            SpaceTreemapPresenter.Render(TreemapCanvas, _visibleItems, ItemsList.SelectedItem as SpaceDisplayItem,
                SelectFromTreemap, NavigateFromTreemap, ShowActions);
    }

    private void View_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateViewLayout(e.NewSize.Width);

    private void TreemapToggle_Click(object sender, RoutedEventArgs e)
    {
        _showTreemap = TreemapToggle.IsChecked == true;
        UpdateViewLayout(ActualWidth);
    }

    private void UpdateViewLayout(double width)
    {
        var wide = width >= 840;
        var showMap = _showTreemap ?? wide;
        var showList = wide || !showMap;
        TreemapToggle.IsChecked = showMap;
        ListPane.Visibility = showList ? Visibility.Visible : Visibility.Collapsed;
        MapPane.Visibility = showMap ? Visibility.Visible : Visibility.Collapsed;
        ListColumn.Width = showList ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MapColumn.Width = showMap ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MapPane.Margin = new Thickness(showList ? 10 : 0, 0, 0, 0);
        UpdateEmptyState();
    }

    private void LocationFilter_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyLocationFilter((ItemsList.SelectedItem as SpaceDisplayItem)?.Path);

    private void SelectFromTreemap(SpaceDisplayItem item)
    {
        ItemsList.SelectedItem = item;
        ItemsList.ScrollIntoView(item);
    }

    private void NavigateFromTreemap(SpaceDisplayItem item)
    {
        if (item.IsDirectory)
            TreemapCanvas.CaptureMouse();
        ActivateItem(item);
    }

    private void ShowActions(SpaceDisplayItem item)
    {
        if (Window.GetWindow(this) is not Window owner || owner is not IPluginSearchWindow view)
            return;
        var fullPath = item.Path;
        var result = new AppSearchResult
        {
            Name = item.Name,
            FullPath = fullPath,
            ParentDir = System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty,
            IsDir = item.IsDirectory,
            Drive = (System.IO.Path.GetPathRoot(fullPath) ?? string.Empty).TrimEnd('\\', '/'),
            ResultKind = "File"
        };
        ActionFlyout.Show([result], view, owner, TreemapCanvas, PlacementMode.MousePoint);
    }

    private void UpdateEmptyState()
    {
        var noChartData = ListPane.Visibility == Visibility.Collapsed && !_visibleItems.Any(item => item.Size > 0);
        EmptyState.Visibility = _isLoading || _visibleItems.Count == 0 || noChartData ? Visibility.Visible : Visibility.Collapsed;
        if (_isLoading)
            EmptyText.Text = TranslationManager.Instance["Space_Loading"];
        else if (_loadFailed)
            EmptyText.Text = TranslationManager.Instance["Space_LoadFailed"];
        else if (_history.Count == 1 && _items.Count == 0)
            EmptyText.Text = TranslationManager.Instance["Space_NoIndexes"];
        else if (_items.Count == 0)
            EmptyText.Text = TranslationManager.Instance["Space_Empty"];
        else if (_visibleItems.Count == 0)
            EmptyText.Text = TranslationManager.Instance["Space_NoMatches"];
        else if (noChartData)
            EmptyText.Text = TranslationManager.Instance["Space_NoChartData"];
    }

    private void UpdateSummary()
    {
        var total = _items.Aggregate(0L, static (sum, item) => sum > long.MaxValue - item.Size ? long.MaxValue : sum + item.Size);
        LogicalSizeHint.Text = TranslationManager.Instance[_history.Count == 1 ? "Space_VolumeCapacityHint" : "Space_LogicalSize"];
        SummaryText.Text = string.IsNullOrWhiteSpace(LocationFilter.Text)
            ? string.Format(TranslationManager.Instance["Space_Summary"], _items.Count, SpaceSizeFormatter.Format(total))
            : string.Format(TranslationManager.Instance["Space_FilterSummary"], _visibleItems.Count, _items.Count);
    }

    private void SetLoading(bool loading)
    {
        _isLoading = loading;
        BackButton.IsEnabled = !loading && _history.Count > 1;
        ParentListButton.IsEnabled = !loading && _history.Count > 1;
        ItemsList.IsEnabled = !loading;
        LocationFilter.IsEnabled = !loading;
        OpenSelectionButton.IsEnabled = !loading && ItemsList.SelectedItem != null;
        SummaryText.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
        UpdateEmptyState();
    }

    private void OnThemeChanged() => RenderTreemap();

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_history.Count > 0)
            _history[0] = _history[0] with { Name = TranslationManager.Instance["Space_Home"] };
        ItemsList.Items.Refresh();
        RebuildBreadcrumbs();
        UpdateEmptyState();
        UpdateSummary();
        RenderTreemap();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _refreshWatcher.Dispose();
        _previewSupport.Dispose();
        _searchService.Dispose();
        ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        IsVisibleChanged -= OnIsVisibleChanged;
        ItemsList.ItemsSource = null;
        TreemapCanvas.Children.Clear();
        _items = Array.Empty<SpaceDisplayItem>();
        _visibleItems = Array.Empty<SpaceDisplayItem>();
        _history.Clear();
        ShellIconHelper.ClearCache();
        PathCacheMaintenance.ClearAllPathCaches();
        Core.Win32Api.TrimWorkingSet();
    }

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenSelectionButton.IsEnabled = !_isLoading && ItemsList.SelectedItem != null;
        RenderTreemap();
        _previewSupport.SelectionChanged((ItemsList.SelectedItem as SpaceDisplayItem)?.Path);
    }
    private void OpenSelection_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsList.SelectedItem is SpaceDisplayItem item)
            ActivateItem(item);
    }
    private void ItemsList_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ItemsList.SelectedItem is not SpaceDisplayItem item) return;
        e.Handled = true;
        ActivateItem(item);
    }
    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ItemsList.SelectedItem is SpaceDisplayItem item)
            ActivateItem(item);
    }
    private void ItemsList_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = TreeWalk.Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (row?.Content is not SpaceDisplayItem item)
            return;
        ItemsList.SelectedItem = item;
        ShowActions(item);
        e.Handled = true;
    }
    private void TreemapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderTreemap();
    private void TreemapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!TreemapCanvas.IsMouseCaptured)
            return;
        TreemapCanvas.ReleaseMouseCapture();
        e.Handled = true;
    }
    private void Back_Click(object sender, RoutedEventArgs e) => NavigateToHistory(_history.Count - 2);
}
