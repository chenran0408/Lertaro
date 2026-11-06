using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Windows;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// The dedicated clipboard panel: search box, kind tabs, a grouped list (pinned section, then date
/// groups), a preview pane and the keyboard surface. Window lifecycle and row rebuilding live here;
/// everything that acts on the selected rows lives in <see cref="ClipboardPanelActions"/>.
/// </summary>
/// <remarks>
/// The window lifecycle follows CalendarView: one <see cref="PluginWindow"/> re-created on demand,
/// the whole list rebuilt on every change instead of kept in sync (a few hundred rows at most).
/// The list reloads on every window activation, so a copy made elsewhere shows up when the panel
/// is summoned again without any background refresh machinery.
/// </remarks>
public partial class ClipboardPanelView : UserControl
{
    private static PluginWindow? _window;
    private static ClipboardPanelView? _instance;

    private ClipboardPanelTab _tab = ClipboardPanelTab.All;
    private readonly ClipboardPanelActions _actions;
    private bool _loading;

    // State shared with ClipboardPanelActions (which reads it through the view reference).
    internal List<ClipboardPanelItem> _items = [];
    internal Dictionary<long, ClipboardEntry> _entryById = [];
    internal bool PreviewVisible { get; set; } = true;

    // The preview pane's note editor, tracked so an in-progress edit is flushed before the pane is
    // rebuilt for another row or the panel closes (LostFocus alone misses both).
    internal TextBox? NoteEditor;
    internal long NoteEntryId;
    internal string NoteAtLoad = string.Empty;

    // The pane's width while visible, remembered across a collapse so toggling it back restores
    // what the user had dragged rather than a default.
    internal double _previewWidth = 300;

    public ClipboardPanelView()
    {
        InitializeComponent();
        ClipboardPanelLabels.ApplyTo(this);
        _actions = new ClipboardPanelActions(this);
        TranslationService.CultureChanged += OnCultureChanged;
        // Reopen where the user left off: tab and sort direction survive summons and restarts.
        SortToggle.IsChecked = ClipboardPanelViewSettings.SortByFrequency();
        ApplyStoredTab();
    }

    private void ApplyStoredTab()
    {
        var target = (ClipboardPanelTab)ClipboardPanelViewSettings.LastTab() switch
        {
            ClipboardPanelTab.Text => TabText,
            ClipboardPanelTab.Image => TabImage,
            ClipboardPanelTab.Files => TabFiles,
            ClipboardPanelTab.Favorites => TabFavorites,
            _ => TabAll,
        };
        if (target.IsChecked != true) target.IsChecked = true; // fires Tab_Checked
        _tab = target == TabText ? ClipboardPanelTab.Text
            : target == TabImage ? ClipboardPanelTab.Image
            : target == TabFiles ? ClipboardPanelTab.Files
            : target == TabFavorites ? ClipboardPanelTab.Favorites
            : ClipboardPanelTab.All;
    }

    /// <summary>Opens the panel near the cursor, or brings the existing one to the front. True when
    /// a panel is now visible -- the host's Win+V path treats false as "fall back to the Quick
    /// Panel tab".</summary>
    public static bool TryShow()
    {
        var app = Application.Current;
        if (app == null) return false;
        if (!app.Dispatcher.CheckAccess())
        {
            // The hotkey IPC lands on a non-UI thread; marshal and wait, because the caller's
            // fallback decision needs to know whether the panel actually opened.
            return app.Dispatcher.Invoke(() => TryShow());
        }

        // Toggle: the wake key pressed while the panel is up closes it. Activating an already
        // focused window reads as a dead key to the user.
        if (_window is { IsVisible: true })
        {
            _instance?._actions.FlushNote();
            _window.Close();
            return true;
        }

        if (_window == null || _instance == null)
        {
            var view = new ClipboardPanelView();
            var window = new PluginWindow(
                ClipboardPanelLabels.Title(), view.StoredWidth(), view.StoredHeight(), PluginWindowMode.Window);
            view.PreviewVisible = ClipboardPanelViewSettings.PreviewVisible();
            view.PreviewToggle.IsChecked = view.PreviewVisible;
            view._previewWidth = ClipboardPanelViewSettings.PreviewWidth();
            view.PreviewColumn.Width = new GridLength(view.PreviewVisible ? view._previewWidth : 0);
            window.ShowFooter = false;
            window.ContentHostControl.VerticalContentAlignment = VerticalAlignment.Stretch;
            window.ContentHostControl.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            window.ContentHostControl.Content = view;
            window.Closed += (_, _) =>
            {
                ClipboardPanelViewSettings.Save(window.Width, window.Height, view.PreviewVisible);
                ClipboardPanelViewSettings.SavePreviewWidth(view.PreviewVisible ? view.PreviewColumn.Width.Value : view._previewWidth);
                _window = null;
                _instance = null;
            };
            var (left, top) = ClipboardPanelPlacement.Place(window.Width, window.Height);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
            _window = window;
            _instance = view;
            // A copy made while the panel was closed shows up the moment the panel is summoned
            // again -- no background refresh machinery needed for a summon-and-paste surface. The
            // activation handler is the single reload path: Show/Activate fire it, so no direct
            // call here (it used to double- and triple-load the store on every open).
            window.Activated += (_, _) => _ = view.ReloadAsync();
            window.Show();
        }

        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _instance.SearchBox.Text = string.Empty;
        _window.Activate();
        _instance.SearchBox.Focus();
        return true;
    }

    internal void ClosePanel()
    {
        _actions.FlushNote();
        _window?.Close();
    }

    private void OnCultureChanged(string _) =>
        Dispatcher.BeginInvoke(() =>
        {
            ClipboardPanelLabels.ApplyTo(this);
            RebuildRows(Entries);
        });

    internal IReadOnlyList<ClipboardEntry> Entries { get; private set; } = [];

    /// <summary>Saved window size, honored once at window creation.</summary>
    internal double StoredWidth() => Math.Max(420, ClipboardPanelViewSettings.Width());
    internal double StoredHeight() => Math.Max(420, ClipboardPanelViewSettings.Height());

    private void SortToggle_Click(object sender, RoutedEventArgs e)
    {
        ClipboardPanelViewSettings.SaveSortByFrequency(SortToggle.IsChecked == true);
        RebuildRows(Entries);
    }

    internal async Task ReloadAsync()
    {
        // Single-flight with a trailing coalesce: overlapping triggers (rapid re-activation,
        // a delete landing mid-load) collapse into one trailing reload instead of racing rebuilds.
        if (_reloading) { _reloadQueued = true; return; }
        _reloading = true;
        try
        {
            do
            {
                _reloadQueued = false;
                // _loading keeps SearchBox_TextChanged from rebuilding against the previous
                // snapshot while the fetch is in flight; the fetch's own rebuild covers it.
                _loading = true;
                try
                {
                    Entries = await ClipboardRuntime.Instance.GetEntriesAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    Entries = [];
                }
                _loading = false;
                RebuildRows(Entries);
            }
            while (_reloadQueued);
        }
        finally { _reloading = false; }

        // Warm the thumbnail cache in the background so the first scroll through image rows does
        // not decode them one by one on the UI thread. Bounded: recent rows first, and the cache
        // itself clears wholesale past its cap.
        var imagesDirectory = ClipboardPanelActions.ImagesDirectory();
        var names = Entries.Where(entry => entry.Kind == ClipboardEntryKind.Image)
            .Take(24).Select(entry => entry.ImagePath).ToList();
        if (names.Count > 0)
            _ = Task.Run(() => ThumbnailImageSourceConverter.PrimeCache(imagesDirectory, names));
    }

    private bool _reloading;
    private bool _reloadQueued;

    private ClipboardPanelTab CurrentTab() =>
        TabText.IsChecked == true ? ClipboardPanelTab.Text
        : TabImage.IsChecked == true ? ClipboardPanelTab.Image
        : TabFiles.IsChecked == true ? ClipboardPanelTab.Files
        : TabFavorites.IsChecked == true ? ClipboardPanelTab.Favorites
        : ClipboardPanelTab.All;

    private void RebuildRows(IReadOnlyList<ClipboardEntry> entries)
    {
        var query = SearchBox.Text.Trim();
        entries = ClipboardPanelRows.Sort(entries, SortToggle.IsChecked == true);
        _items = ClipboardPanelRows.Build(
            entries, _tab, query, FuzzyMatchService.IsMatch,
            ClipboardPanelLabels.Subtitle,
            entry => ClipboardPanelLabels.DateGroup(entry.LastSeenAtMs),
            ClipboardPanelLabels.PinnedGroup());
        var selectedId = SelectedId();
        _entryById = entries.ToDictionary(entry => entry.Id);
        Lst.ItemsSource = _items;
        ClipboardPanelLabels.SetEmpty(this, _items.Count == 0);
        ClearSearchButton.Visibility = query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreSelection(selectedId);
        _actions.EndKeyboardSelection();
        _actions.UpdateHintBar();
    }

    private long SelectedId() => Lst.SelectedItem is ClipboardPanelItem { IsRow: true } item ? item.Id : 0;

    private void RestoreSelection(long id)
    {
        long target = id;
        if (target == 0) target = _actions._selectAfterReload;
        if (target != 0)
        {
            var index = _items.FindIndex(item => item.Id == target);
            if (index >= 0)
            {
                _actions._selectAfterReload = 0;
                Lst.SelectedIndex = index;
                return;
            }
        }
        _actions._selectAfterReload = 0;
        SelectFirstRow();
    }

    /// <summary>Selects the first row (skipping headers); also the reset point after a filter change.</summary>
    private void SelectFirstRow()
    {
        for (var index = 0; index < _items.Count; index++)
        {
            if (!_items[index].IsHeader)
            {
                Lst.SelectedIndex = index;
                return;
            }
        }
        Lst.SelectedIndex = -1;
    }

    // --- events (thin forwards to the loader/actions) ---

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        RebuildRows(Entries);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_instance == null) return;
        _tab = CurrentTab();
        ClipboardPanelViewSettings.SaveLastTab((int)_tab);
        RebuildRows(Entries);
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }

    private void Lst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Lst.SelectedItem is ClipboardPanelItem { IsHeader: true })
            SelectFirstRow();
        _actions.UpdateHintBar();
        _actions.UpdatePreview();
    }

    private void Lst_MouseDoubleClick(object sender, MouseButtonEventArgs e) => _actions.PasteSelected(plainText: false);

    private void Lst_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_actions.SelectedEntry() is not { } entry)
        {
            e.Handled = true;
            return;
        }
        Lst.ContextMenu = _actions.BuildContextMenu().Build(entry);
    }

    private Point _dragAnchor;

    private void Lst_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _actions.EndKeyboardSelection(); // a mouse press re-seeds any later Shift+arrow extension
        _dragAnchor = e.GetPosition(this);
    }

    private void Lst_PreviewMouseMove(object sender, MouseEventArgs e) =>
        _actions.BeginDragIfRequested(e, _dragAnchor);

    // Type-to-search (Maccy/Ditto behavior): once the user starts typing while the list -- not the
    // search box -- has focus, the keystroke seeds the search box instead of vanishing. Digits
    // never reach here: the preview-keydown resolver claims 1~9 as quick paste and marks them
    // handled first, so slot paste keeps precedence over search seeding.
    private void Lst_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        var desired = PreviewToggle.IsChecked == true;
        if (desired != PreviewVisible) _actions.TogglePreview(); // flips and syncs the button
        else PreviewToggle.IsChecked = PreviewVisible;           // re-sync a stray programmatic set
    }

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var result = ClipboardPanelKeys.Resolve(e.Key, Keyboard.Modifiers, SearchBox.IsKeyboardFocused,
            SearchBox.IsKeyboardFocused && SearchBox.Text.Length > 0);
        switch (result.Action)
        {
            case ClipboardPanelAction.None:
                return;
            case ClipboardPanelAction.MoveUp:
                _actions.MoveSelection(-1);
                break;
            case ClipboardPanelAction.MoveDown:
                _actions.MoveSelection(+1);
                break;
            case ClipboardPanelAction.ExtendUp:
                _actions.ExtendSelection(-1);
                break;
            case ClipboardPanelAction.ExtendDown:
                _actions.ExtendSelection(+1);
                break;
            case ClipboardPanelAction.SelectAll:
                _actions.SelectAllRows();
                break;
            case ClipboardPanelAction.ClearSearch:
                // Esc's first press: empty the query, keep the panel up and the caret in the box so
                // the next keystrokes are a fresh search; the second press falls through to Close.
                SearchBox.Text = string.Empty;
                break;
            case ClipboardPanelAction.PasteSelected:
                _actions.PasteSelected(plainText: false);
                break;
            case ClipboardPanelAction.PasteSelectedPlainText:
                _actions.PasteSelected(plainText: true);
                break;
            case ClipboardPanelAction.DeleteSelected:
                _actions.DeleteSelected();
                break;
            case ClipboardPanelAction.TogglePinSelected:
                _actions.TogglePinSelected();
                break;
            case ClipboardPanelAction.Close:
                ClosePanel();
                break;
            case ClipboardPanelAction.FocusSearch:
                SearchBox.Focus();
                break;
            case ClipboardPanelAction.PasteByIndex:
                _actions.PasteByIndex(result.Index);
                break;
        }
        e.Handled = true;
    }
}
