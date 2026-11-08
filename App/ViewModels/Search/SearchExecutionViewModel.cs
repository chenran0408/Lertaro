using System.Windows;
using System.Windows.Threading;
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
    private readonly DispatcherTimer _providerLoadedRefreshTimer;

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
        _providerLoadedRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
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
                    QueueSuggest();
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
        // Only check for web mode when the query has a space (keyword + text pattern). Single-word
        // queries skip UserSettings.Load() entirely, keeping the setter fast for normal typing.
        var space = query.IndexOf(' ');
        if (space < 0) { WebModeKeyword = null; return; }
        string keyword = query[..space];
        string rest = query[(space + 1)..].Trim();

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

    // B1 suggest: default OFF (enabling sends typed text to the engine). A 200ms debounce after
    // the last keystroke, one flight at a time; failures fail silent -- the web row stays.
    private static readonly System.Net.Http.HttpClient SuggestClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private DispatcherTimer? _suggestTimer;
    private int _suggestFlight;
    private List<AppSearchResult> _suggestionRows = [];

    private void QueueSuggest()
    {
        if (!UserSettings.Load().WebSuggestEnabled) return;
        _suggestTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _suggestTimer.Tick += async (_, _) =>
        {
            _suggestTimer.Stop();
            if (!IsWebMode || WebModeKeyword is not { } keyword) return;
            var rest = _searchQuery[(keyword.Length + 1)..].Trim();
            if (rest.Length == 0) { ReplaceSuggestions([]); return; }
            var match = UserSettings.Load().WebKeywords.First(w => w.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            var endpoint = SuggestEndpointFor(match.Url, rest);
            if (endpoint == null) return;
            var flight = Interlocked.Increment(ref _suggestFlight);
            try
            {
                var json = await SuggestClient.GetStringAsync(endpoint).ConfigureAwait(true);
                var names = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                // [query, [suggestions]] (google/bing/opensearch all fit this shape)
                if (names.ValueKind != System.Text.Json.JsonValueKind.Array || names.GetArrayLength() < 2) return;
                var list = new List<AppSearchResult>();
                foreach (var item in names[1].EnumerateArray())
                {
                    var text = item.GetString();
                    if (string.IsNullOrEmpty(text)) continue;
                    list.Add(SearchResultHelper.CreateWebSuggestionResult(text, keyword));
                    if (list.Count >= 6) break;
                }
                if (Volatile.Read(ref _suggestFlight) == flight)
                    ReplaceSuggestions(list);
            }
            catch { /* network/parse failure: the plain web row remains */ }
        };
        _suggestTimer.Start();
    }

    private void ReplaceSuggestions(List<AppSearchResult> rows)
    {
        _suggestionRows = rows;
        var webRow = SearchResultHelper.CreateWebSearchResult(_searchQuery);
        _dispatcher.ReplaceResultsNow([webRow, .. rows]);
    }

    private static string? SuggestEndpointFor(string urlTemplate, string query) =>
        urlTemplate.Contains("google.com/search") ? "https://suggestqueries.google.com/complete/search?client=firefox&q=" + Uri.EscapeDataString(query)
        : urlTemplate.Contains("bing.com/search") ? "https://api.bing.com/osjson.aspx?query=" + Uri.EscapeDataString(query)
        : urlTemplate.Contains("wikipedia.org") ? "https://en.wikipedia.org/w/api.php?action=opensearch&limit=6&search=" + Uri.EscapeDataString(query)
        : null; // user-added engines: local-only

    /// <summary>Opens the current web-mode query in the browser and reports whether it did.</summary>
    public bool OpenWebSearch()
    {
        if (!IsWebMode || WebModeKeyword is not { } keyword) return false;
        var match = UserSettings.Load().WebKeywords.First(w => w.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        var rest = _searchQuery[(keyword.Length + 1)..].Trim();
        UrlLauncher.Open(match.Url.Replace("{query}", Uri.EscapeDataString(rest)));
        return true;
    }

    /// <summary>Opens the engine for one suggestion text; returns whether web mode was active.</summary>
    public bool OpenWebSearchFor(string suggestion)
    {
        if (!IsWebMode || WebModeKeyword is not { } keyword) return false;
        var match = UserSettings.Load().WebKeywords.First(w => w.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));
        UrlLauncher.Open(match.Url.Replace("{query}", Uri.EscapeDataString(suggestion)));
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
