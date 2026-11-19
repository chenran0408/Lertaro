using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Windows;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// The dedicated clipboard panel: search box, kind tabs, a grouped list (pinned section, then date
/// groups), a preview pane and the keyboard surface. Window lifecycle and row rebuilding live here;
/// everything that acts on the selected rows lives in <see cref="ClipboardPanelActions"/>.
/// </summary>
public partial class ClipboardPanelView : UserControl
{
    private static PluginWindow? _window;
    private static ClipboardPanelView? _instance;

    private ClipboardPanelTab _tab = ClipboardPanelTab.All;
    private readonly ClipboardPanelActions _actions;
    private bool _rebuildingRows;
    private int _snapshotRefreshQueued;
    private bool _dialogOpen;
    internal SourceWindow? PasteTarget { get; private set; }
    internal bool PastePending { get; set; }
    private CancellationTokenSource? _pendingFilter;
    internal bool SearchPending => _pendingFilter != null;
    private readonly DispatcherTimer _captureStatusTimer = new();

    // State shared with ClipboardPanelActions (which reads it through the view reference).
    internal List<ClipboardPanelItem> _items = [];
    internal Dictionary<long, ClipboardEntry> _entryById = [];
    internal bool PreviewVisible { get; set; } = true;
    internal bool IsCompactLayout { get; private set; }
    internal bool IsCompactPreviewOpen { get; private set; }
    internal bool IsPreviewShown => IsCompactLayout ? IsCompactPreviewOpen : PreviewVisible;

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
        _captureStatusTimer.Tick += (_, _) => RefreshCaptureStatus();
        Loaded += ViewLoaded;
        Unloaded += ViewUnloaded;
        // Reopen where the user left off: tab and sort direction survive summons and restarts.
        SortToggle.IsChecked = ClipboardPanelViewSettings.SortByFrequency();
        ClipboardPanelLabels.SetSort(this);
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
    /// a panel is now visible -- the host's clipboard shortcut treats false as "fall back to the Quick
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
            _instance?.ClosePanel();
            return true;
        }

        if (_window == null || _instance == null)
        {
            var anchor = ClipboardPanelPlacement.ReadAnchor();
            var view = new ClipboardPanelView
            {
                PasteTarget = ClipboardRuntime.Instance.CapturePasteTarget(),
            };
            var savedWidth = view.StoredWidth();
            var savedHeight = view.StoredHeight();
            var window = new PluginWindow(ClipboardPanelLabels.Title(), savedWidth, savedHeight, PluginWindowMode.Window)
            {
                MinWidth = 420,
                MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            view.PreviewVisible = ClipboardPanelViewSettings.PreviewVisible();
            view._previewWidth = ClipboardPanelViewSettings.PreviewWidth();
            window.ShowFooter = false;
            window.ContentHostControl.VerticalContentAlignment = VerticalAlignment.Stretch;
            window.ContentHostControl.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            window.ContentHostControl.Content = view;
            ClipboardPanelPlacement.PositionHandle(window, anchor, savedWidth, savedHeight);
            var openedWidth = window.Width;
            var openedHeight = window.Height;
            view.ApplyPreviewLayout(openedWidth);
            window.Closing += (_, _) =>
            {
                view._closing = true;
                view._actions.FlushNote();
            };
            window.Closed += (_, _) =>
            {
                ClipboardPanelViewSettings.Save(
                    window.Width == openedWidth ? savedWidth : window.Width,
                    window.Height == openedHeight ? savedHeight : window.Height,
                    view.PreviewVisible);
                ClipboardPanelViewSettings.SavePreviewWidth(view._previewWidth);
                _window = null;
                _instance = null;
            };
            // Mature-manager behavior: the panel is a transient surface -- clicking into another
            // window dismisses it. Guarded, because deactivate can fire while an in-flight close
            // is still settling, and a second Close on a closing window throws.
            window.Deactivated += (_, _) => { if (!view._dialogOpen) view.ClosePanel(); };

            _window = window;
            _instance = view;
            window.Show();
        }

        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _instance.SearchBox.Text = string.Empty;
        _window.Activate();
        _instance.SearchBox.Focus();
        return true;
    }

    private bool _closing;

    internal void ClosePanel()
    {
        if (_closing) return;
        _window?.Close();
    }

    private void OnCultureChanged(string _) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing) return;
            ClipboardPanelLabels.ApplyTo(this);
            RebuildRows(Entries);
            RefreshCaptureStatus();
        });

    internal IReadOnlyList<ClipboardEntry> Entries { get; private set; } = [];

    /// <summary>Saved window size, honored once at window creation.</summary>
    internal double StoredWidth() => Math.Max(420, ClipboardPanelViewSettings.Width());
    internal double StoredHeight() => Math.Max(300, ClipboardPanelViewSettings.Height());

    private void SortToggle_Click(object sender, RoutedEventArgs e)
    {
        ClipboardPanelViewSettings.SaveSortByFrequency(SortToggle.IsChecked == true);
        ClipboardPanelLabels.SetSort(this);
        RebuildRows(Entries);
    }

    private void ViewLoaded(object sender, RoutedEventArgs e)
    {
        TranslationService.CultureChanged += OnCultureChanged;
        var runtime = ClipboardRuntime.Instance;
        runtime.EntriesChanged += QueueSnapshotRefresh;
        ApplySnapshot(runtime.Entries);
        RefreshCaptureStatus();
    }

    private void ViewUnloaded(object sender, RoutedEventArgs e)
    {
        _captureStatusTimer.Stop();
        _pendingFilter?.Cancel();
        _pendingFilter = null;
        ClipboardRuntime.Instance.EntriesChanged -= QueueSnapshotRefresh;
        TranslationService.CultureChanged -= OnCultureChanged;
    }

    internal void QueueSnapshotRefresh()
    {
        if (Interlocked.Exchange(ref _snapshotRefreshQueued, 1) != 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref _snapshotRefreshQueued, 0);
            if (_closing || !IsLoaded || NoteEditor?.IsKeyboardFocusWithin == true) return;

            var latest = ClipboardRuntime.Instance.Entries;
            if (!ReferenceEquals(Entries, latest))
                ApplySnapshot(latest);
            RefreshCaptureStatus();
        }));
    }

    internal void ApplySnapshot(IReadOnlyList<ClipboardEntry> entries)
    {
        Entries = entries;
        RebuildRows(entries);
    }

    private ClipboardPanelTab CurrentTab() =>
        TabText.IsChecked == true ? ClipboardPanelTab.Text
        : TabImage.IsChecked == true ? ClipboardPanelTab.Image
        : TabFiles.IsChecked == true ? ClipboardPanelTab.Files
        : TabFavorites.IsChecked == true ? ClipboardPanelTab.Favorites
        : ClipboardPanelTab.All;

    private void RebuildRows(IReadOnlyList<ClipboardEntry> entries)
    {
        _pressedRow = null;
        _wheelRemainder = 0;
        _pendingFilter?.Cancel();
        _pendingFilter = null;
        var query = SearchBox.Text.Trim();
        var hasSearchText = SearchBox.Text.Length > 0;
        SearchWatermark.Visibility = hasSearchText ? Visibility.Collapsed : Visibility.Visible;
        ClearSearchButton.Visibility = hasSearchText ? Visibility.Visible : Visibility.Collapsed;

        var sorted = ClipboardPanelRows.Sort(entries, SortToggle.IsChecked == true);
        var tab = _tab;
        var scoped = sorted.Where(entry => ClipboardPanelRows.MatchesTab(entry, tab)).ToArray();
        if (query.Length == 0 || scoped.Length == 0)
        {
            ApplyRows(scoped, query);
            return;
        }

        var pending = new CancellationTokenSource();
        _pendingFilter = pending;
        Lst.IsEnabled = false;
        EmptyText.Text = TranslationService.Get("ClipboardHistory_PanelSearching");
        EmptyText.Visibility = Visibility.Visible;
        _actions.UpdateHintBar();
        _ = FilterRowsAsync(scoped, query, pending);
    }

    private async Task FilterRowsAsync(ClipboardEntry[] entries, string query, CancellationTokenSource pending)
    {
        using (pending)
        {
            try
            {
                await Task.Delay(75, pending.Token).ConfigureAwait(false);
                var matches = await Task.Run(() => MatchRows(entries, query, pending.Token), pending.Token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_pendingFilter, pending)) return;
                    _pendingFilter = null;
                    ApplyRows(matches, query);
                }).Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (pending.IsCancellationRequested || Dispatcher.HasShutdownStarted) { }
            catch (Exception ex)
            {
                Lertaro.PluginSdk.Logger.Log($"[ClipboardHistory] Search failed: {ex.GetType().Name}.", Lertaro.PluginSdk.LogLevel.Warn);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_pendingFilter, pending)) return;
                    _pendingFilter = null;
                    ApplyRows([], query);
                    EmptyText.Text = TranslationService.Get("ClipboardHistory_PanelSearchFailed");
                }).Task.ConfigureAwait(false);
            }
        }
    }

    private static ClipboardEntry[] MatchRows(IEnumerable<ClipboardEntry> entries, string query, CancellationToken token)
    {
        var matches = new List<ClipboardEntry>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (ClipboardPanelRows.MatchesQuery(entry, query, FuzzyMatchService.IsMatch))
                matches.Add(entry);
        }
        return matches.ToArray();
    }

    private void ApplyRows(ClipboardEntry[] entries, string query)
    {
        _items = ClipboardPanelRows.BuildVisible(
            entries, query,
            ClipboardPanelLabels.Subtitle,
            entry => ClipboardPanelLabels.DateGroup(entry.LastSeenAtMs),
            ClipboardPanelLabels.Tooltip,
            ClipboardPanelLabels.PinnedGroup());
        var selectedIds = Lst.SelectedItems.OfType<ClipboardPanelItem>().Where(item => item.IsRow).Select(item => item.Id).ToHashSet();
        _entryById = entries.ToDictionary(entry => entry.Id);
        _rebuildingRows = true;
        try
        {
            Lst.ItemsSource = _items;
            RestoreSelection(selectedIds);
        }
        finally { _rebuildingRows = false; }

        Lst.IsEnabled = true;
        ClipboardPanelLabels.SetEmpty(this, _items.Count == 0 && !ClipboardRuntime.Instance.StorageFailed, query.Length > 0);
        _actions.EndKeyboardSelection();
        _actions.UpdateHintBar();
        _actions.UpdatePreview();
    }

    private void RestoreSelection(HashSet<long> selectedIds)
    {
        foreach (var row in _items)
            if (row.IsRow && selectedIds.Contains(row.Id))
                Lst.SelectedItems.Add(row);

        var fallback = _actions._selectAfterReload;
        _actions._selectAfterReload = 0;
        if (Lst.SelectedItems.Count > 0) return;
        var index = fallback == 0 ? -1 : _items.FindIndex(item => item.Id == fallback);
        if (index >= 0)
            Lst.SelectedIndex = index;
        else
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
        CloseCompactPreview();
        RebuildRows(Entries);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_instance == null) return;
        _tab = CurrentTab();
        ClipboardPanelViewSettings.SaveLastTab((int)_tab);
        CloseCompactPreview();
        RebuildRows(Entries);
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }

    private void Lst_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rebuildingRows) return;
        if (Lst.SelectedItem is ClipboardPanelItem { IsHeader: true })
            SelectFirstRow();
        _actions.UpdateHintBar();
        _actions.UpdatePreview();
    }

    private ClipboardPanelItem? RowAt(DependencyObject? source) =>
        source != null && ItemsControl.ContainerFromElement(Lst, source) is ListBoxItem { Content: ClipboardPanelItem { IsRow: true } row }
            ? row
            : null;

    private void Lst_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (SearchPending)
        {
            e.Handled = true;
            return;
        }
        if (e.CursorLeft >= 0)
        {
            if (RowAt(Mouse.DirectlyOver as DependencyObject) is not { } row)
            {
                e.Handled = true;
                return;
            }
            SelectContextRow(row);
        }

        var selected = _actions.SelectedEntries();
        if (selected.Count == 0)
        {
            e.Handled = true;
            return;
        }
        _actions.BuildContextMenu().Populate(Lst.ContextMenu, selected);
    }

    internal void SelectContextRow(ClipboardPanelItem row)
    {
        if (Lst.SelectedItems.Contains(row)) return;
        Lst.SelectedItems.Clear();
        Lst.SelectedItem = row;
    }

    private (long Id, Point Point)? _pressedRow;
    private int _wheelRemainder;

    private void Lst_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _actions.EndKeyboardSelection();
        _pressedRow = !SearchPending && Keyboard.Modifiers == ModifierKeys.None &&
            RowAt(e.OriginalSource as DependencyObject) is { } row
            ? (row.Id, e.GetPosition(this)) : null;
    }

    private void Lst_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var press = _pressedRow;
        _pressedRow = null;
        if (!SearchPending && press is { } clicked && Keyboard.Modifiers == ModifierKeys.None
            && RowAt(e.OriginalSource as DependencyObject)?.Id == clicked.Id
            && !MovedPastDragThreshold(clicked.Point, e.GetPosition(this)))
        {
            e.Handled = true;
            _actions.PasteEntry(clicked.Id);
        }
    }

    private void Lst_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedRow is not { } press) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pressedRow = null;
            return;
        }
        if (!MovedPastDragThreshold(press.Point, e.GetPosition(this))) return;

        // DragDrop pumps messages, including a possible button release, before returning.
        _pressedRow = null;
        if (!SearchPending && Keyboard.Modifiers == ModifierKeys.None)
            _actions.DragEntry(press.Id);
    }

    private static bool MovedPastDragThreshold(Point start, Point current) =>
        Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private void Lst_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SearchPending || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        NavigateWheel(e.Delta);
    }

    internal void NavigateWheel(int delta)
    {
        const int wheelStep = 120;
        if (Math.Sign(delta) != Math.Sign(_wheelRemainder)) _wheelRemainder = 0;
        _wheelRemainder += delta;
        var steps = _wheelRemainder / wheelStep;
        _wheelRemainder %= wheelStep;
        var direction = -Math.Sign(steps);
        for (var step = 0; step < Math.Abs(steps); step++)
            _actions.MoveSelection(direction);
    }

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

    private void PasteButton_Click(object sender, RoutedEventArgs e) => _actions.PasteSelected(plainText: false);

    private void CopyButton_Click(object sender, RoutedEventArgs e) => _actions.CopySelected();

    internal void RefreshCaptureStatus()
    {
        _captureStatusTimer.Stop();
        var remaining = ClipboardRuntime.CapturePausedUntil();
        if (ClipboardRuntime.Instance.StorageFailed)
        {
            CaptureNotice.Text = TranslationService.Get("ClipboardHistory_StorageFailed");
            EmptyText.Visibility = Visibility.Collapsed;
        }
        else if (!ClipboardSettings.Enabled)
        {
            CaptureNotice.Text = TranslationService.Get("ClipboardHistory_PanelRecordingOff");
        }
        else if (remaining is { } until)
        {
            CaptureNotice.Text = ClipboardPanelLabels.PausedUntil(until.ToLocalTime().ToString("t"));
            _captureStatusTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, (until - DateTimeOffset.UtcNow).TotalMilliseconds));
            _captureStatusTimer.Start();
        }
        else
        {
            CaptureNotice.Text = string.Empty;
        }
        CaptureNotice.Visibility = CaptureNotice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HistoryMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = HistoryMenuButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        _actions.BuildContextMenu().PopulateHistory(menu);
        menu.IsOpen = true;
    }

    internal T RunOwnedDialog<T>(Func<T> show)
    {
        _dialogOpen = true;
        try { return show(); }
        finally { _dialogOpen = false; }
    }

    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        TogglePreview();
        SearchBox.Focus();
    }

    internal void TogglePreview()
    {
        if (IsCompactLayout)
            IsCompactPreviewOpen = !IsCompactPreviewOpen;
        else
            PreviewVisible = !PreviewVisible;
        ApplyPreviewLayout(_layoutWidth);
    }

    private void CloseCompactPreview()
    {
        if (!IsCompactPreviewOpen) return;
        IsCompactPreviewOpen = false;
        ApplyPreviewLayout(_layoutWidth);
    }

    private void PaneGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) ApplyPreviewLayout(e.NewSize.Width);
    }

    private void PreviewSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _previewWidth = PreviewColumn.Width.Value;
    }

    private double _layoutWidth;

    internal void ApplyPreviewLayout(double width)
    {
        _layoutWidth = width;
        var compact = width < 620;
        if (!compact) IsCompactPreviewOpen = false;
        IsCompactLayout = compact;
        Grid.SetColumnSpan(TypeFilters, compact ? 2 : 1);
        Grid.SetRow(ToolbarActions, compact ? 1 : 0);
        Grid.SetColumn(ToolbarActions, compact ? 0 : 1);
        Grid.SetColumnSpan(ToolbarActions, compact ? 2 : 1);
        var sideBySide = PreviewVisible && !compact;
        var visibility = IsPreviewShown ? Visibility.Visible : Visibility.Collapsed;
        var previewChanged = PreviewHost.Visibility != visibility;
        PreviewHost.Visibility = visibility;
        HistoryHost.Visibility = IsCompactPreviewOpen ? Visibility.Collapsed : Visibility.Visible;
        PreviewToggle.IsChecked = IsPreviewShown;
        ClipboardPanelLabels.SetPreview(this);
        PreviewSplitter.Visibility = sideBySide ? Visibility.Visible : Visibility.Collapsed;
        HistoryColumn.MinWidth = sideBySide ? 280 : 0;
        PreviewColumn.MinWidth = sideBySide ? 180 : 0;
        PreviewColumn.Width = new GridLength(sideBySide ? Math.Min(_previewWidth, width - HistoryColumn.MinWidth - PreviewSplitter.Width) : 0);
        Grid.SetColumn(PreviewHost, compact ? 0 : 2);
        PreviewHost.Margin = compact ? new Thickness(12, 0, 12, 8) : new Thickness(4, 0, 12, 8);
        _actions.UpdateHintBar();
        if (previewChanged && IsPreviewShown) _actions.UpdatePreview();
        if (previewChanged && !IsPreviewShown) _actions.FlushNote();
    }

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsCompactPreviewOpen && e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            CloseCompactPreview();
            SearchBox.Focus();
            e.Handled = true;
            return;
        }
        var focus = SearchBox.IsKeyboardFocusWithin ? ClipboardPanelFocus.Search
            : Lst.IsKeyboardFocusWithin ? ClipboardPanelFocus.List : ClipboardPanelFocus.Other;
        var result = ClipboardPanelKeys.Resolve(e.Key, Keyboard.Modifiers, focus, SearchBox.Text.Length > 0);
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
            case ClipboardPanelAction.MoveTop:
                _actions.MoveSelectionEdge(-1);
                break;
            case ClipboardPanelAction.MoveBottom:
                _actions.MoveSelectionEdge(+1);
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
                ClearSearch_Click(this, e);
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
