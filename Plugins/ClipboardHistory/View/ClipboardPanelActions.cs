using System.IO;
using System.Windows;
using System.Windows.Input;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// The panel's action surface: everything that acts on the selected rows (paste in both formats,
/// merged multi-paste, delete, pin, copy, source window, path-format copies, save image as) plus
/// the preview pane, the hint bar, the context menu payload and drag-out. Split from the view for
/// the per-file line limit; the only state it keeps is the keyboard selection cursor for
/// Shift+arrow extensions -- everything else reads through the view.
/// </summary>
internal sealed class ClipboardPanelActions(ClipboardPanelView view)
{
    public ClipboardPanelContextMenu BuildContextMenu() =>
        new(
            () => PasteSelected(plainText: false),
            () => SelectedEntries().ToList().ForEach(entry => _ = ClipboardRuntime.Instance.CopyAsync(entry.Id)),
            TogglePinSelected,
            DeleteSelected,
            ClearSelectedNote,
            () => SelectedEntries().ToList().ForEach(entry => ClipboardRuntime.Instance.OpenSource(entry.Id)),
            text => _ = ClipboardRuntime.Instance.CopyTextAsync(text),
            SaveSelectedImageAs,
            ClearHistoryWithConfirm,
            TogglePreview,
            TogglePauseCapture);

    // --- selection ---

    /// <summary>Every selected row's entry, in display order (headers are not selectable).</summary>
    public List<ClipboardEntry> SelectedEntries() =>
        view.Lst.SelectedItems.OfType<ClipboardPanelItem>()
            .Where(item => item.IsRow && view._entryById.TryGetValue(item.Id, out _))
            .Select(item => view._entryById[item.Id])
            .ToList();

    public ClipboardEntry? SelectedEntry() => SelectedEntries() is [var single] ? single : null;

    public void MoveSelection(int delta)
    {
        var items = view._items;
        if (items.Count == 0) return;
        var next = view.Lst.SelectedIndex + delta;
        while (next >= 0 && next < items.Count && items[next].IsHeader)
            next += delta;
        if (next < 0 || next >= items.Count) return;
        EndKeyboardSelection();
        view.Lst.SelectedIndex = next;
        view.Lst.ScrollIntoView(view.Lst.SelectedItem);
    }

    // Keyboard selection state: _cursor is where the next Shift+arrow moves from, _anchor is the
    // end the extension grows from (WPF's Extended mode exposes no anchor for programmatic
    // selection, so the panel tracks its own). Rebuilds and mouse presses end the session.
    private int _cursor = -1;
    private int _anchor = -1;
    private bool _extending;

    /// <summary>Ends any in-flight Shift+arrow extension: the next one re-seeds from whatever the
    /// list currently shows. Rebuilds and mouse presses call this.</summary>
    public void EndKeyboardSelection()
    {
        _extending = false;
        _cursor = -1;
        _anchor = -1;
    }

    /// <summary>Shift+arrow: grow the selection from the anchor to the moved cursor, headers
    /// skipped, like every mainstream manager's list. A fresh extension seeds the anchor from the
    /// current selection, so Ctrl+click followed by Shift+arrow behaves natively too.</summary>
    public void ExtendSelection(int delta)
    {
        var items = view._items;
        if (items.Count == 0) return;
        if (!_extending)
        {
            _cursor = view.Lst.SelectedIndex;
            if (_cursor < 0 || _cursor >= items.Count || items[_cursor].IsHeader)
            {
                MoveSelection(delta);
                return;
            }
            _anchor = _cursor;
        }
        var target = _cursor + delta;
        while (target >= 0 && target < items.Count && items[target].IsHeader)
            target += delta;
        if (target < 0 || target >= items.Count) return;
        var range = ClipboardPanelRows.RowIndexesBetween(_anchor, target, items);
        if (range.Count == 0) return;
        view.Lst.SelectedItems.Clear();
        foreach (var index in range)
            view.Lst.SelectedItems.Add(items[index]);
        _cursor = target;
        _extending = true;
        view.Lst.ScrollIntoView(items[target]);
    }

    /// <summary>Ctrl+A: select every row in the current view (headers excluded); collapses back to
    /// nothing extra when everything is already selected.</summary>
    public void SelectAllRows()
    {
        var rows = view._items.Where(item => item.IsRow).ToList();
        if (rows.Count == 0) return;
        var allSelected = view.Lst.SelectedItems.Count == rows.Count;
        view.Lst.SelectedItems.Clear();
        if (!allSelected)
            foreach (var item in rows)
                view.Lst.SelectedItems.Add(item);
    }

    // --- paste ---

    public void PasteSelected(bool plainText)
    {
        var selected = SelectedEntries();
        if (selected.Count == 0) return;

        // Multi-select pastes the merged payload (texts joined by line breaks, file paths unioned).
        if (selected.Count > 1)
        {
            if (ClipboardRuntime.Instance.PasteEntries(selected, keepFormat: !plainText))
                view.ClosePanel();
            return;
        }
        PasteAndMaybeClose(selected[0].Id, plainText);
    }

    public void PasteByIndex(int oneBased)
    {
        var selectable = ClipboardPanelRows.SelectableIds(view._items);
        if (oneBased < 1 || oneBased > selectable.Count) return;
        PasteAndMaybeClose(selectable[oneBased - 1]);
    }

    private void PasteAndMaybeClose(long id, bool plainText = false)
    {
        // Pinned rows keep the panel open (the pin means "use repeatedly"); everything else closes
        // so the injected Ctrl+V lands in the window the user was typing in.
        if (ClipboardRuntime.Instance.PasteEntries([view._entryById[id]], keepFormat: !plainText))
            view.ClosePanel();
    }

    // --- row actions ---

    public void DeleteSelected()
    {
        if (view.Lst.SelectedItem is not ClipboardPanelItem { IsRow: true } item) return;
        ClipboardRuntime.Instance.Delete(item.Id);
        _ = view.ReloadAsync();
    }

    public void TogglePinSelected()
    {
        if (view.Lst.SelectedItem is not ClipboardPanelItem { IsRow: true } item) return;
        ClipboardRuntime.Instance.TogglePin(item.Id);
        _ = view.ReloadAsync();
    }

    /// <summary>Toggles a 10-minute capture pause straight from the panel, no settings trip.</summary>
    public void TogglePauseCapture()
    {
        if (ClipboardRuntime.IsCapturePaused())
            ClipboardRuntime.PauseCaptureFor(TimeSpan.Zero);
        else
            ClipboardRuntime.PauseCaptureFor(TimeSpan.FromMinutes(10));
    }

    public void ClearHistoryWithConfirm()
    {
        var result = PluginMessageBoxService.Show(
            TranslationService.Get("ClipboardHistory_PanelClearConfirm"),
            TranslationService.Get("ClipboardHistory_PluginName"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;
        ClipboardRuntime.Instance.ClearHistory();
        _ = view.ReloadAsync();
    }

    /// <summary>Saves the selected image row's PNG to a user-chosen file.</summary>
    public void SaveSelectedImageAs()
    {
        var entry = SelectedEntries().FirstOrDefault(item => item.Kind == ClipboardEntryKind.Image);
        if (entry == null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            Filter = "PNG|*.png",
            DefaultExt = ".png",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            File.Copy(ImageFilePathFor(entry), dialog.FileName, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PasteBackHelper.Notify("ClipboardHistory_PasteFailed");
        }
    }

    // --- preview and hint bar ---

    public void UpdatePreview()
    {
        if (!view.PreviewVisible) return;
        var entry = SelectedEntry();
        FlushNote();
        view.PreviewTitle.Text = entry is null ? string.Empty : ClipboardPanelLabels.Subtitle(entry);
        view.NoteEditor = ClipboardPanelPreview.Apply(view.PreviewContent, view.PreviewTitle, entry, ImagesDirectory(),
            text => { if (entry != null && text != entry.Note) ClipboardRuntime.Instance.SetNote(entry.Id, text); });
        view.NoteEntryId = entry?.Id ?? 0;
        view.NoteAtLoad = entry?.Note ?? string.Empty;
    }

    /// <summary>Commits an in-progress note edit before the pane is rebuilt (selection change,
    /// panel close) so typing followed by an immediate click on another row is not lost.</summary>
    public void FlushNote()
    {
        if (view.NoteEditor is not { } editor) return;
        if (!string.Equals(editor.Text, view.NoteAtLoad, StringComparison.Ordinal))
            ClipboardRuntime.Instance.SetNote(view.NoteEntryId, editor.Text);
        view.NoteEditor = null;
    }

    /// <summary>Context menu: wipes the selected entry's note and reflects it in the open editor
    /// (the store update still rides SetNote; the editor edit keeps the pane visually honest).</summary>
    public void ClearSelectedNote()
    {
        if (SelectedEntries() is not [var entry]) return;
        ClipboardRuntime.Instance.SetNote(entry.Id, string.Empty);
        if (view.NoteEditor is { } editor && view.NoteEntryId == entry.Id)
        {
            editor.Text = string.Empty;
            view.NoteAtLoad = string.Empty;
        }
    }

    /// <summary>The hint bar doubles as the multi-select status line: with more than one row
    /// selected it says so, because "paste" now means "paste merged" and that must be legible.</summary>
    public void UpdateHintBar()
    {
        var selected = view.Lst.SelectedItems.OfType<ClipboardPanelItem>().Count(item => item.IsRow);
        view.HintBar.Text = selected > 1
            ? string.Format(TranslationService.Get("ClipboardHistory_PanelSelectedMerge"), selected)
            : ClipboardPanelLabels.HintBar();
    }

    /// <summary>Collapses or restores the preview column; the choice persists across summons.</summary>
    public void TogglePreview()
    {
        view.PreviewVisible = !view.PreviewVisible;
        view.PreviewColumn.Width = view.PreviewVisible ? new GridLength(300) : new GridLength(0);
        if (view.PreviewVisible) UpdatePreview();
    }

    // --- drag out ---

    /// <summary>Starts a drag from the selected row when the left button has moved past the drag
    /// threshold: text rows drag their text, file rows their drop list, image rows the stored PNG
    /// file itself (already on disk, content-addressed). Returns true when a drag started.</summary>
    public bool BeginDragIfRequested(MouseEventArgs e, Point dragAnchor)
    {
        if (e.LeftButton != MouseButtonState.Pressed || SelectedEntry() is not { } entry) return false;
        if ((e.GetPosition(view) - dragAnchor).Length < 4) return false;

        var data = BuildDragData(entry);
        if (data == null) return false;
        DragDrop.DoDragDrop(view.Lst, data, DragDropEffects.Copy);
        return true;
    }

    private static DataObject? BuildDragData(ClipboardEntry entry) => entry.Kind switch
    {
        ClipboardEntryKind.Text => new DataObject(DataFormats.UnicodeText, entry.Content),
        ClipboardEntryKind.FilePaths => MakeFileDrop(entry.PathList),
        ClipboardEntryKind.Image when File.Exists(ImageFilePathFor(entry)) =>
            MakeFileDrop([ImageFilePathFor(entry)]),
        _ => null,
    };

    private static DataObject MakeFileDrop(IReadOnlyList<string> paths)
    {
        var list = new System.Collections.Specialized.StringCollection();
        list.AddRange(paths.ToArray());
        var data = new DataObject();
        data.SetFileDropList(list);
        return data;
    }

    // --- shared paths ---

    public static string ImagesDirectory()
    {
        var directory = UserDataService.GetUserDataDirectory() ?? string.Empty;
        return Path.Combine(directory, "ClipboardHistory", "images");
    }

    public static string ImageFilePathFor(ClipboardEntry entry) =>
        Path.Combine(ImagesDirectory(), entry.ImagePath);
}
