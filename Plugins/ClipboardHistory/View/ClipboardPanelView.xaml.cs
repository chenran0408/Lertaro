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

    public ClipboardPanelView()
    {
        InitializeComponent();
        ClipboardPanelLabels.ApplyTo(this);
        _actions = new ClipboardPanelActions(this);
        TranslationService.CultureChanged += OnCultureChanged;
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
            _window.Close();
            return true;
        }

        if (_window == null || _instance == null)
        {
            var view = new ClipboardPanelView();
            var window = new PluginWindow(
                ClipboardPanelLabels.Title(), view.StoredWidth(), view.StoredHeight(), PluginWindowMode.Window);
            view.PreviewVisible = ClipboardPanelViewSettings.PreviewVisible();
            if (!view.PreviewVisible) view.PreviewColumn.Width = new GridLength(0);
            window.ShowFooter = false;
            window.ContentHostControl.VerticalContentAlignment = VerticalAlignment.Stretch;
            window.ContentHostControl.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            window.ContentHostControl.Content = view;
            window.Closed += (_, _) =>
            {
                ClipboardPanelViewSettings.Save(window.Width, window.Height, view.PreviewVisible);
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
            // again -- no background refresh machinery needed for a summon-and-paste surface.
            window.Activated += (_, _) => _ = view.ReloadAsync();
            _ = view.ReloadAsync();
            window.Show();
        }

        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _instance.SearchBox.Text = string.Empty;
        _ = _instance.ReloadAsync();
        _window.Activate();
        _instance.SearchBox.Focus();
        return true;
    }

    internal void ClosePanel() => _window?.Close();

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

    private void SortToggle_Click(object sender, RoutedEventArgs e) =>
        RebuildRows(Entries);

    internal async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            Entries = await ClipboardRuntime.Instance.GetEntriesAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception)
        {
            Entries = [];
        }
        finally
        {
            _loading = false;
        }

        RebuildRows(Entries);
    }

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
        _actions.UpdateHintBar();
    }

    private long SelectedId() => Lst.SelectedItem is ClipboardPanelItem { IsRow: true } item ? item.Id : 0;

    private void RestoreSelection(long id)
    {
        if (id != 0)
        {
            var index = _items.FindIndex(item => item.Id == id);
            if (index >= 0)
            {
                Lst.SelectedIndex = index;
                return;
            }
        }
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

    private void Lst_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragAnchor = e.GetPosition(this);

    private void Lst_PreviewMouseMove(object sender, MouseEventArgs e) =>
        _actions.BeginDragIfRequested(e, _dragAnchor);

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var result = ClipboardPanelKeys.Resolve(e.Key, Keyboard.Modifiers, SearchBox.IsKeyboardFocused);
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
