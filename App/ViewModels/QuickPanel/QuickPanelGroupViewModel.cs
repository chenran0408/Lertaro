using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.QuickPanel;

/// <summary>One source's worth of the quick panel, with its own order.</summary>
public class QuickPanelGroupViewModel : ViewModelBase
{
    // Enough rows or tiles to cover the largest normal panel viewport plus a small scroll-ahead buffer.
    // The outer ScrollViewer asks for another page as it approaches the end, so making this larger only
    // creates hidden WPF containers and starts icon loads the user cannot see yet.
    private const int InitialMaterializedItemCount = 32;
    private readonly List<(AppSearchResult Item, DateTime? Modified)> _loaded;
    private readonly int _maxItems;
    private int _matchingCount;
    private int _materializedItemCount = InitialMaterializedItemCount;

    public QuickPanelGroupViewModel(
        string sourceId,
        string title,
        string folderPath,
        List<(AppSearchResult Item, DateTime? Modified)> loaded,
        QuickPanelSortMode sortMode = QuickPanelSortMode.ModifiedDescending,
        bool thumbnailView = true,
        bool expanded = true,
        bool acceptsDrops = false,
        bool showsHeading = true,
        bool isLoading = false,
        int maxItems = 0,
        IEnumerable<string>? itemOrder = null,
        IReadOnlyDictionary<string, int>? gridSlots = null,
        IReadOnlyDictionary<string, QuickPanelItemLabel>? itemLabels = null)
    {
        ItemLabels = new QuickPanelItemLabels(itemLabels);
        ItemLabels.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(ItemLabels)); Rebuild(); };
        SourceId = sourceId;
        Title = title;
        FolderPath = folderPath;
        AcceptsDrops = acceptsDrops;
        ShowsHeading = showsHeading;
        _maxItems = maxItems;
        _loaded = loaded;
        _itemOrder = itemOrder?.ToList() ?? new();
        _gridSlots = new Dictionary<string, int>(gridSlots ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        // The fields, not the properties: each setter rebuilds, and the group has nothing to rebuild
        // from until the call below.
        _sortMode = sortMode;
        _isThumbnailView = thumbnailView;
        _isExpanded = expanded;
        _isLoading = isLoading;
        Rebuild();
    }

    /// <summary>The source this group came from, which is what its stored preferences are filed under.</summary>
    public string SourceId { get; }

    public QuickPanelItemLabels ItemLabels { get; }

    /// <summary>The heading: the user's own name for the source, or the source's default one.</summary>
    public string Title { get; }

    /// <summary>Whether the heading's name is drawn at all.</summary>
    /// <remarks>
    /// False for the one group a plugin tab holds: the tab is already named after it, and a heading
    /// repeating that name is the panel saying the same word twice, one line apart. Only the name goes --
    /// the count, the sort toggle, the view toggle and the collapse arrow all still apply.
    /// </remarks>
    public bool ShowsHeading { get; }

    /// <summary>The folder itself, shown in full beside the heading, and where a drop lands.</summary>
    public string FolderPath { get; }

    /// <summary>Whether files dragged onto this group add shortcuts in its folder.</summary>
    public bool AcceptsDrops { get; }

    private bool _isDropTarget;

    /// <summary>True while a droppable drag is over this group, which is what the outline is bound to.</summary>
    /// <remarks>
    /// On the view model rather than a visual state on the Expander: what counts as droppable is a
    /// question about this group (does it accept drops, is the drag carrying files, did it come from
    /// inside the panel), and the answer has to be worked out before anything can be drawn either way.
    /// </remarks>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }

    /// <summary>How many entries this group is showing, which under a filter is how many matched.</summary>
    public int Count => DisplayCount;

    private string _filter = string.Empty;

    /// <summary>Narrows the group to the entries whose name the query matches.</summary>
    public void ApplyFilter(string? query)
    {
        var normalized = query?.Trim() ?? string.Empty;
        if (string.Equals(_filter, normalized, StringComparison.Ordinal)) return;

        _filter = normalized;
        if (_filter.Length > 0) IsArranging = false;
        OnPropertyChanged(nameof(CanArrange));
        Rebuild(resetMaterialization: true);
    }

    /// <summary>Whether anything survived the filter -- a group with nothing left is hidden entirely.</summary>
    public bool HasMatches => _matchingCount > 0 || (AcceptsDrops && _filter.Length == 0);

    private bool _isLoading;

    /// <summary>
    /// Keeps a large source's visual tree bounded. The full sorted set stays in memory, while only the
    /// next page becomes WPF containers when the enclosing scroll viewer approaches this group.
    /// </summary>
    public bool LoadNextPage()
    {
        if (_materializedItemCount >= DisplayCount)
            return false;

        _materializedItemCount += InitialMaterializedItemCount;
        Rebuild();
        return true;
    }

    /// <summary>Releases rows beyond the first viewport page while retaining the complete loaded set.</summary>
    public void ResetMaterialization()
    {
        _materializedItemCount = InitialMaterializedItemCount;
        if (Items.Count > InitialMaterializedItemCount)
            Items.ReplaceRange(Items.Take(InitialMaterializedItemCount).ToList());
    }

    /// <summary>Takes a freshly loaded set in place of what this group was holding.</summary>
    /// <remarks>
    /// The same group object, refilled, rather than a new one: its sort, its view and whether it is
    /// collapsed are the user's own doing and belong to the group, not to the entries in it. A drop that
    /// rebuilt the group would land the files and quietly undo everything else about it.
    /// </remarks>
    public void Replace(List<(AppSearchResult Item, DateTime? Modified)> loaded)
    {
        _loaded.Clear();
        _loaded.AddRange(loaded);
        _isLoading = false;
        OnPropertyChanged(nameof(CanArrange));
        Rebuild(resetMaterialization: true);
    }

    /// <summary>
    /// Adds a bounded arrival-order batch while enumeration is still running. The completed source is
    /// sorted once through <see cref="Replace"/>, so an intermediate batch never claims final order.
    /// </summary>
    public void AppendLoading(List<(AppSearchResult Item, DateTime? Modified)> loaded)
    {
        _loaded.AddRange(loaded);
        Rebuild();
    }

    public ObservableRangeCollection<AppSearchResult> Items { get; } = new();

    private QuickPanelSortMode _sortMode = QuickPanelSortMode.ModifiedDescending;

    /// <summary>This group's own order. Newest first until someone says otherwise.</summary>
    public QuickPanelSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (!SetProperty(ref _sortMode, value)) return;
            if (value != QuickPanelSortMode.Custom) IsArranging = false;
            Rebuild();
        }
    }

    public IReadOnlyList<Settings.QuickPanel.QuickPanelSortOption> SortOptions => Settings.QuickPanel.QuickPanelSortOption.All;

    private List<string> _itemOrder;
    public IReadOnlyList<string> ItemOrder => _itemOrder;
    public bool CanArrange => !_isLoading && _filter.Length == 0;
    private bool _isArranging;
    public bool IsArranging
    {
        get => _isArranging;
        set
        {
            if (value && !CanArrange) return;
            if (value)
            {
                EnsureGridSlots();
                SortMode = QuickPanelSortMode.Custom;
            }
            SetProperty(ref _isArranging, value);
        }
    }

    public void MoveBefore(string path, string? beforePath)
        => Move(path, beforePath, insertBefore: true);

    public void MoveTo(string path, string? targetPath)
        => Move(path, targetPath, insertBefore: false);

    private void Move(string path, string? beforePath, bool insertBefore)
    {
        if (!CanArrange) return;
        var paths = OrderedItems().Select(pair => pair.Item.FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var from = paths.FindIndex(candidate => candidate.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (from < 0 || string.Equals(path, beforePath, StringComparison.OrdinalIgnoreCase)) return;
        if (beforePath != null && !paths.Contains(beforePath, StringComparer.OrdinalIgnoreCase)) return;
        var to = beforePath == null ? paths.Count - 1 : paths.FindIndex(candidate => candidate.Equals(beforePath, StringComparison.OrdinalIgnoreCase));
        if (insertBefore && beforePath != null && from < to) to--;
        paths.RemoveAt(from);
        paths.Insert(to, path);
        // Keep absent entries in the saved arrangement until their source returns them again.
        var present = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _itemOrder = paths.Concat(_itemOrder.Where(saved => present.Add(saved))).ToList();
        _sortMode = QuickPanelSortMode.Custom;
        Rebuild();
        OnPropertyChanged(nameof(SortMode));
        OnPropertyChanged(nameof(ItemOrder));
    }

    private Dictionary<string, int> _gridSlots;
    public IReadOnlyDictionary<string, int> GridSlots => _gridSlots;
    public bool UsesGridSlots => SortMode == QuickPanelSortMode.Custom && _isThumbnailView && CanArrange;

    public void PlaceInGrid(IEnumerable<string> paths, int slot)
    {
        if (!CanArrange || slot < 0) return;
        EnsureGridSlots();
        _gridSlots = QuickPanelGridLayout.Place(_gridSlots, paths, slot);
        _itemOrder = _gridSlots.OrderBy(pair => pair.Value).Select(pair => pair.Key).ToList();
        _sortMode = QuickPanelSortMode.Custom;
        Rebuild();
        OnPropertyChanged(nameof(SortMode));
        OnPropertyChanged(nameof(GridSlots));
        OnPropertyChanged(nameof(ItemOrder));
    }

    private void EnsureGridSlots() => _gridSlots = QuickPanelGridLayout.Merge(_gridSlots, OrderedItems().Select(pair => pair.Item.FullPath));

    private IEnumerable<(AppSearchResult Item, DateTime? Modified)> OrderedItems()
        => QuickPanelItemOrdering.Order(_loaded, SortMode, _itemOrder);

    public void ToggleSort() => SortMode = SortMode == QuickPanelSortMode.ModifiedDescending
        ? QuickPanelSortMode.NameAscending
        : QuickPanelSortMode.ModifiedDescending;

    public System.Windows.Input.ICommand ToggleSortCommand
        => _toggleSortCommand ??= new RelayCommand(ToggleSort);

    private System.Windows.Input.ICommand? _toggleSortCommand;

    private bool _isThumbnailView = true;

    /// <summary>Thumbnail tiles when true, the detail list when false. This folder's own choice.</summary>
    /// <remarks>
    /// Per group for the same reason the order is: a folder of images wants tiles and a folder of
    /// documents wants names and dates, and which is which is a property of the folder, not of the
    /// panel. It lived on the panel while every group shared one list, and stayed there one step longer
    /// than it had to.
    /// </remarks>
    public bool IsThumbnailView
    {
        get => _isThumbnailView;
        set
        {
            if (SetProperty(ref _isThumbnailView, value)) OnPropertyChanged(nameof(UsesGridSlots));
        }
    }

    public void ToggleView() => IsThumbnailView = !IsThumbnailView;

    public System.Windows.Input.ICommand ToggleViewCommand
        => _toggleViewCommand ??= new RelayCommand(ToggleView);

    private System.Windows.Input.ICommand? _toggleViewCommand;

    private bool _isExpanded = true;

    /// <summary>Whether the user has expanded this group.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private void Rebuild(bool resetMaterialization = false)
    {
        if (UsesGridSlots) EnsureGridSlots();
        OnPropertyChanged(nameof(GridSlots));
        OnPropertyChanged(nameof(UsesGridSlots));
        if (resetMaterialization)
            _materializedItemCount = InitialMaterializedItemCount;

        var matching = _filter.Length == 0
            ? _loaded
            : _loaded.Where(pair => FuzzyMatcher.IsMatch(_filter, pair.Item.Name) || FuzzyMatcher.IsMatch(_filter, ItemLabels.Name(pair.Item.FullPath, pair.Item.Name, pair.Item.IsDir))).ToList();

        // Ordered on the DateTime, not on the string the row shows: that string is formatted and
        // localised, so ordering by it would rank "3 days ago" against "10 minutes ago" alphabetically
        // and answer differently in every language. Items with no known time sort last either way.
        var ordered = _isLoading ? matching : QuickPanelItemOrdering.Order(matching, SortMode, _itemOrder);

        _matchingCount = matching.Count;
        var visible = ordered.Take(Math.Min(_materializedItemCount, DisplayCount)).Select(pair => pair.Item).ToList();
        if (!Items.SequenceEqual(visible))
            Items.ReplaceRange(visible);

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasMatches));
    }

    private int DisplayCount => _maxItems > 0 ? Math.Min(_matchingCount, _maxItems) : _matchingCount;
}
