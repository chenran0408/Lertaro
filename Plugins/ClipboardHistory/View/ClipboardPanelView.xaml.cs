using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Windows;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// The dedicated clipboard panel: search box, kind tabs, a grouped list (pinned section, then date
/// groups) and the keyboard surface -- Enter pastes, Ctrl+Enter pastes as plain text, Del removes,
/// Ctrl+P pins, and with focus on the list the digits 1~9 paste the Nth row.
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
    private List<ClipboardPanelItem> _items = [];
    private Dictionary<long, ClipboardEntry> _entryById = [];
    private bool _loading;

    public ClipboardPanelView()
    {
        InitializeComponent();
        ClipboardPanelLabels.ApplyTo(this);
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

        if (_window == null || _instance == null)
        {
            var view = new ClipboardPanelView();
            var window = new PluginWindow(
                ClipboardPanelLabels.Title(), 470, 640, PluginWindowMode.Window);
            window.ShowFooter = false;
            window.ContentHostControl.VerticalContentAlignment = VerticalAlignment.Stretch;
            window.ContentHostControl.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            window.ContentHostControl.Content = view;
            window.Closed += (_, _) =>
            {
                _window = null;
                _instance = null;
            };
            var (left, top) = ClipboardPanelPlacement.Place(window.Width, window.Height);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = left;
            window.Top = top;
            _window = window;
            _instance = view;
            _ = view.ReloadAsync();
            window.Show();
        }

        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _ = _instance.ReloadAsync();
        _window.Activate();
        _instance.SearchBox.Focus();
        return true;
    }

    private void OnCultureChanged(string _) =>
        Dispatcher.BeginInvoke(() =>
        {
            ClipboardPanelLabels.ApplyTo(this);
            RebuildRows(Entries);
        });

    internal IReadOnlyList<ClipboardEntry> Entries { get; private set; } = [];

    private async Task ReloadAsync()
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

    // --- events ---

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
    }

    private void Lst_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PasteSelected(plainText: false);

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var result = ClipboardPanelKeys.Resolve(e.Key, Keyboard.Modifiers, SearchBox.IsKeyboardFocused);
        switch (result.Action)
        {
            case ClipboardPanelAction.None:
                return;
            case ClipboardPanelAction.MoveUp:
                MoveSelection(-1);
                break;
            case ClipboardPanelAction.MoveDown:
                MoveSelection(+1);
                break;
            case ClipboardPanelAction.PasteSelected:
                PasteSelected(plainText: false);
                break;
            case ClipboardPanelAction.PasteSelectedPlainText:
                PasteSelected(plainText: true);
                break;
            case ClipboardPanelAction.DeleteSelected:
                DeleteSelected();
                break;
            case ClipboardPanelAction.TogglePinSelected:
                TogglePinSelected();
                break;
            case ClipboardPanelAction.Close:
                _window?.Close();
                break;
            case ClipboardPanelAction.FocusSearch:
                SearchBox.Focus();
                break;
            case ClipboardPanelAction.PasteByIndex:
                PasteByIndex(result.Index);
                break;
        }
        e.Handled = true;
    }

    // --- actions ---

    private void MoveSelection(int delta)
    {
        if (_items.Count == 0) return;
        var current = Lst.SelectedIndex;
        var next = current + delta;
        while (next >= 0 && next < _items.Count && _items[next].IsHeader)
            next += delta;
        if (next < 0 || next >= _items.Count) return;
        Lst.SelectedIndex = next;
        Lst.ScrollIntoView(Lst.SelectedItem);
    }

    private void PasteSelected(bool plainText)
    {
        var selected = SelectedEntries();
        if (selected.Count == 0) return;

        // Multi-select pastes the merged payload (texts joined by line breaks, file paths unioned).
        if (selected.Count > 1)
        {
            if (ClipboardRuntime.Instance.PasteEntries(selected) && _window != null)
                _window.Close();
            return;
        }
        PasteAndMaybeClose(selected[0].Id);
    }

    /// <summary>Every selected row's entry, in display order (headers are not selectable).</summary>
    private List<ClipboardEntry> SelectedEntries() =>
        Lst.SelectedItems.OfType<ClipboardPanelItem>()
            .Where(item => item.IsRow && _entryById.TryGetValue(item.Id, out _))
            .Select(item => _entryById[item.Id])
            .ToList();

    private void PasteByIndex(int oneBased)
    {
        var selectable = ClipboardPanelRows.SelectableIds(_items);
        if (oneBased < 1 || oneBased > selectable.Count) return;
        PasteAndMaybeClose(selectable[oneBased - 1]);
    }

    private void PasteAndMaybeClose(long id)
    {
        // Pinned rows keep the panel open (the pin means "use repeatedly"); everything else closes
        // so the injected Ctrl+V lands in the window the user was typing in.
        if (ClipboardRuntime.Instance.PasteAndClose(id) && _window != null)
            _window.Close();
    }

    private void DeleteSelected()
    {
        if (Lst.SelectedItem is not ClipboardPanelItem { IsRow: true } item) return;
        ClipboardRuntime.Instance.Delete(item.Id);
        _ = ReloadAsync();
    }

    private void TogglePinSelected()
    {
        if (Lst.SelectedItem is not ClipboardPanelItem { IsRow: true } item) return;
        ClipboardRuntime.Instance.TogglePin(item.Id);
        _ = ReloadAsync();
    }
}
