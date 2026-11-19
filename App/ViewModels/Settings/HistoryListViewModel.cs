using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;

using Lertaro.Core.SearchIndex;
namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// Backs the reusable history list UI (search box, scrollable entries, remove/clear, enable toggle).
/// Shared by the "search history" and "keyword history" tabs -- each supplies its own storage and how
/// a raw stored entry (a <see cref="PluginSdk.Services.HistoryEntry"/> for search history, a
/// <see cref="KeywordHistoryEntry"/> for keyword history) maps to a displayable row, and gets it back
/// unchanged from <see cref="GetEntriesToSave"/> to persist.
/// </summary>
public class HistoryListViewModel<T> : ViewModelBase
{
    private readonly Func<IReadOnlyList<T>> _loadEntries;
    private readonly Func<T, HistoryEntryViewModel<T>> _mapEntry;
    private readonly Func<bool> _getEnabled;
    private readonly Action<bool> _setEnabled;
    private readonly Func<HistoryEntryViewModel<T>, string> _key;
    private readonly HashSet<string> _pendingRemovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HistoryEntryViewModel<T>> _allItems = new();
    private string _searchText = string.Empty;

    public HistoryListViewModel(
        Func<IReadOnlyList<T>> loadEntries,
        Func<T, HistoryEntryViewModel<T>> mapEntry,
        Func<bool> getEnabled,
        Action<bool> setEnabled,
        Func<T, string>? entryKey = null)
    {
        _loadEntries = loadEntries;
        _mapEntry = mapEntry;
        _getEnabled = getEnabled;
        _setEnabled = setEnabled;
        _key = entryKey == null ? row => row.Primary : row => entryKey(row.RawValue);

        foreach (var raw in _loadEntries())
            _allItems.Add(_mapEntry(raw));

        FilteredItems = new ObservableCollection<HistoryEntryViewModel<T>>(_allItems);
        RemoveItemCommand = new RelayCommand<HistoryEntryViewModel<T>>(RemoveItem);
        ClearAllCommand = new RelayCommand(ClearAll, () => _allItems.Count > 0);
        ClearBelowCommand = new RelayCommand(ClearBelow, () => _allItems.Any(item => item.UsageCount <= UsageThreshold));
    }

    public bool IsHistoryEnabled
    {
        get => _getEnabled();
        set
        {
            if (_getEnabled() != value)
            {
                _setEnabled(value);
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<HistoryEntryViewModel<T>> FilteredItems { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ClearBelowCommand { get; }

    // Threshold for "clear records used at most N times". Fixed to a 1..20 dropdown so the user can
    // never type an out-of-range or non-numeric value; the clear rule is "at most" (<=), not "fewer than".
    public IReadOnlyList<int> UsageThresholds { get; } = Enumerable.Range(1, 20).ToList();

    private int _usageThreshold = 1;
    public int UsageThreshold
    {
        get => _usageThreshold;
        set
        {
            if (SetProperty(ref _usageThreshold, value))
            {
                OnPropertyChanged(nameof(ClearBelowButtonLabel));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ClearBelowButtonLabel => string.Format(
        TranslationManager.Instance["Settings_History_Clear_Below"],
        UsageThreshold);

    public string ResultSummary => string.Format(TranslationManager.Instance["History_ResultSummary"], FilteredItems.Count, _allItems.Count);
    public bool HasPendingRemovals => _pendingRemovals.Count > 0;
    public string PendingSummary => string.Format(TranslationManager.Instance["History_PendingRemovals"], _pendingRemovals.Count);

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(ClearBelowButtonLabel));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(PendingSummary));
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    private void RemoveItem(HistoryEntryViewModel<T>? item)
    {
        if (item == null || !_allItems.Remove(item)) return;
        _pendingRemovals.Add(_key(item));
        FilteredItems.Remove(item);
        UpdateSummary();
    }

    private void ClearAll() => StageRemovals(_allItems);

    private void ClearBelow() => StageRemovals(_allItems.Where(item => item.UsageCount <= UsageThreshold));

    private void StageRemovals(IEnumerable<HistoryEntryViewModel<T>> entries)
    {
        foreach (var entry in entries)
            _pendingRemovals.Add(_key(entry));
        _allItems.RemoveAll(item => _pendingRemovals.Contains(_key(item)));
        ApplyFilter();
    }

    /// <summary>Re-reads the backing store and rebuilds the rows, keeping the current filter/search.</summary>
    public void RefreshFromStore()
    {
        _allItems.Clear();
        foreach (var raw in _loadEntries())
        {
            var row = _mapEntry(raw);
            if (!_pendingRemovals.Contains(_key(row)))
                _allItems.Add(row);
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        foreach (var item in _allItems)
        {
            // FuzzyMatcher.ComputeBestMatch (same FzfPattern.Parse Core's file search uses) splits a
            // multi-word SearchText into independently-required terms -- a plain .Contains(SearchText)
            // treated the whole typed text (spaces included) as one literal string, so a query like
            // "foo bar" would never match an entry containing both words non-contiguously.
            if (string.IsNullOrEmpty(SearchText) ||
                FuzzyMatcher.ComputeBestMatch(SearchText, item.Primary, new[] { item.Secondary }).IsMatch)
            {
                FilteredItems.Add(item);
            }
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(PendingSummary));
        OnPropertyChanged(nameof(HasPendingRemovals));
        CommandManager.InvalidateRequerySuggested();
    }

    internal void SaveRemovals(Action<IReadOnlySet<string>> remove)
    {
        if (_pendingRemovals.Count == 0) return;
        remove(_pendingRemovals);
        _pendingRemovals.Clear();
        RefreshFromStore();
    }

    /// <summary>Returns the current entries (in their edited order) for the caller to persist.</summary>
    public IEnumerable<T> GetEntriesToSave() => _allItems.Select(x => x.RawValue);
}

/// <summary>One row in the history list -- a file/folder/app path (with a subtitle) or a bare keyword.</summary>
public class HistoryEntryViewModel<T> : ViewModelBase
{
    public required T RawValue { get; init; }
    public string Primary { get; init; } = string.Empty;
    public string Secondary { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "";
    public int UsageCount { get; init; } = 1;
}
