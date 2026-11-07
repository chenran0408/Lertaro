using System.Windows;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search.Dispatch;

using Lertaro.Core.Services.Search;
using UserSettings = Lertaro.Core.UserSettings;

using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search;

public class SearchExecutionViewModel : ViewModelBase, IDisposable
{
    private readonly QuickSearchViewModel _mainVm;
    private readonly SearchExecutionEngine _engine;
    private readonly SearchDispatchController _dispatcher;

    private string _searchQuery = null!;
    private bool _isSearching;
    private bool _isResultsListEnabled = true;
    private AppSearchResult? _selectedResult;

    // UI Panel Visibilities
    private Visibility _resultsPanelVisibility = Visibility.Collapsed;
    private Visibility _resultsSeparatorVisibility = Visibility.Collapsed;
    private string? _searchScope;
    private bool _isInlineSearchContext;
    private readonly System.Windows.Threading.DispatcherTimer _providerLoadedRefreshTimer;

    public SearchExecutionViewModel(QuickSearchViewModel mainVm, SearchService searchService)
    {
        _mainVm = mainVm;
        _engine = new SearchExecutionEngine(searchService);
        Results = new ObservableRangeCollection<AppSearchResult>();

        _dispatcher = new SearchDispatchController(
            _engine,
            _mainVm,
            getSearchScope: () => SearchScope,
            getIsInlineSearchContext: () => IsInlineSearchContext,
            getSearchQuery: () => SearchQuery,
            setIsSearching: v => IsSearching = v,
            setResultsPanelVisibility: v => ResultsPanelVisibility = v,
            setResultsSeparatorVisibility: v => ResultsSeparatorVisibility = v,
            replaceResults: ReplaceResults,
            getResultsCount: () => Results.Count);

        // Coalesce multiple providers finishing their (background, unawaited) load in quick succession
        // (e.g. right after app startup) into a single re-run of the current query, not one per provider.
        _providerLoadedRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _providerLoadedRefreshTimer.Tick += (s, e) =>
        {
            _providerLoadedRefreshTimer.Stop();
            if (!IsActionsMode && !string.IsNullOrWhiteSpace(_searchQuery))
                _dispatcher.DispatchSearch(_searchQuery);
        };
        SearchableItemMapper.ProviderLoaded += OnSearchableItemProviderLoaded;
    }

    // Raised from a background thread (see SearchableItemMapper.ProviderLoaded) whenever a searchable-item
    // provider finishes loading. A query issued before that point silently missed that provider's items
    // (AddSearchableItemResults skips providers that aren't cached yet), so re-run the current query to let
    // those items stream in -- ReplaceResults reconciles in place, so this doesn't reset/flicker the list.
    private void OnSearchableItemProviderLoaded() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
    {
        _providerLoadedRefreshTimer.Stop();
        _providerLoadedRefreshTimer.Start();
    }));

    public ObservableRangeCollection<AppSearchResult> Results { get; }

    public AppSearchResult? SelectedResult
    {
        get => _selectedResult;
        set => SetProperty(ref _selectedResult, value);
    }

    public string? SearchScope
    {
        get => _searchScope;
        set => SetProperty(ref _searchScope, value);
    }

    public bool IsInlineSearchContext
    {
        get => _isInlineSearchContext;
        set => SetProperty(ref _isInlineSearchContext, value);
    }

    private bool _isActionsMode;
    public bool IsActionsMode
    {
        get => _isActionsMode;
        set => SetProperty(ref _isActionsMode, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                if (IsActionsMode)
                {
                    return;
                }
                // B1 web mode: "<keyword> <rest>" with a configured keyword. In web mode the file
                // search stands down and a single web-action row replaces the list; the visual
                // state lives on the search box watermark, and Esc unwinds web mode first (the
                // input handler owns that ladder).
                UpdateWebMode(value);
                if (IsWebMode)
                {
                    _engine.CancelPendingSearch();
                    _dispatcher.ShowWebSearchRow(value);
                    return;
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    _engine.CancelPendingSearch();
                    _dispatcher.PerformSearch(value);
                }
                else
                {
                    _dispatcher.DispatchSearch(value);
                }
            }
        }
    }

    // Null when not in web mode; otherwise the keyword whose web URL the query targets.
    private string? _webModeKeyword;
    public string? WebModeKeyword
    {
        get => _webModeKeyword;
        private set => SetProperty(ref _webModeKeyword, value);
    }

    public bool IsWebMode => WebModeKeyword != null;

    private void UpdateWebMode(string query)
    {
        var space = query.IndexOf(' ');
        string? keyword;
        string? rest;
        if (space >= 0)
        {
            keyword = query[..space];
            rest = query[(space + 1)..].Trim();
        }
        else
        {
            keyword = query;
            rest = string.Empty;
        }

        // Entering web mode requires keyword + space + something to search; keyword alone stays a
        // normal search so the transition point is the space itself.
        if (space > 0 && rest.Length > 0 &&
            UserSettings.Load().WebKeywords.FirstOrDefault(w =>
                w.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase)) is { } match &&
            !string.IsNullOrWhiteSpace(match.Url))
        {
            WebModeKeyword = match.Keyword;
        }
        else
        {
            WebModeKeyword = null;
        }
    }

    /// <summary>Opens the current web-mode query in the browser and reports whether it did.</summary>
    public bool OpenWebSearch()
    {
        if (!IsWebMode || WebModeKeyword is not { } keyword) return false;
        var match = UserSettings.Load().WebKeywords.First(w => w.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        var rest = _searchQuery[(keyword.Length + 1)..].Trim();
        UrlLauncher.Open(match.Url.Replace("{query}", Uri.EscapeDataString(rest)));
        return true;
    }

    /// <summary>Leaves web mode and re-runs the query as a normal file search.</summary>
    public void ExitWebMode()
    {
        WebModeKeyword = null;
        _dispatcher.DispatchSearch(_searchQuery);
    }

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            if (SetProperty(ref _isSearching, value))
            {
                // Keep list enabled during search to prevent Win32 system disabled theme flash and allow immediate navigation
                // IsResultsListEnabled = !value;
            }
        }
    }

    public bool IsResultsListEnabled
    {
        get => _isResultsListEnabled;
        set => SetProperty(ref _isResultsListEnabled, value);
    }

    public Visibility ResultsPanelVisibility
    {
        get => _resultsPanelVisibility;
        set => SetProperty(ref _resultsPanelVisibility, value);
    }

    public Visibility ResultsSeparatorVisibility
    {
        get => _resultsSeparatorVisibility;
        set => SetProperty(ref _resultsSeparatorVisibility, value);
    }

    // SearchQuery's setter only re-runs PerformSearch when the value changes, so re-showing the window
    // while the box stays empty wouldn't otherwise notice anything that changed in the meantime.
    public void RefreshEmptyState()
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
            _dispatcher.PerformSearch(_searchQuery);
    }

    public void PerformSearch(string query) => _dispatcher.PerformSearch(query);

    private void ReplaceResults(IEnumerable<AppSearchResult> results) =>
        SearchResultsReconciler.Replace(Results, results, SelectedResult, v => SelectedResult = v);

    public void CancelPendingSearch() => _engine.CancelPendingSearch();

    // The inline window pre-warms its folder listing the moment its scope is known, i.e. before the user
    // has typed -- see DirectChildrenListingCache for why that is the difference between a first character
    // that matches instantly and one that waits for a whole folder walk.
    public void PrewarmDirectoryListing(string? directory) => _engine.PrewarmDirectoryListing(directory);
    internal void UseDirectoryListingCache(DirectChildrenListingCache cache) => _engine.UseDirectoryListingCache(cache);

    public void Dispose()
    {
        SearchableItemMapper.ProviderLoaded -= OnSearchableItemProviderLoaded;
        _providerLoadedRefreshTimer.Stop();
        _engine.Dispose();
    }
}
