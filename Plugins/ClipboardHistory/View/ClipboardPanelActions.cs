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
            CopySelected,
            TogglePinSelected,
            DeleteSelected,
            ClearSelectedNote,
            ExportHistory,
            ImportHistory,
            () => SelectedEntries().ToList().ForEach(entry => ClipboardRuntime.Instance.OpenSource(entry.Id)),
            text => _ = ClipboardRuntime.Instance.CopyTextAsync(text),
            SaveSelectedImageAs,
            ClearHistoryWithConfirm,
            TogglePauseCapture);

    // --- selection ---

    /// <summary>Every selected row's entry, in display order (headers are not selectable).</summary>
    public List<ClipboardEntry> SelectedEntries()
    {
        var selected = view.Lst.SelectedItems.OfType<ClipboardPanelItem>().ToHashSet();
        return view._items.Where(item => item.IsRow && selected.Contains(item))
            .Select(item => view._entryById[item.Id]).ToList();
    }

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

    /// <summary>Home/End: jump to the first or last row, headers skipped along the way.</summary>
    public void MoveSelectionEdge(int direction)
    {
        var items = view._items;
        if (items.Count == 0) return;
        var index = direction < 0 ? 0 : items.Count - 1;
        while (index >= 0 && index < items.Count && items[index].IsHeader)
            index += direction;
        if (index < 0 || index >= items.Count) return;
        EndKeyboardSelection();
        view.Lst.SelectedIndex = index;
        view.Lst.ScrollIntoView(view.Lst.SelectedItem);
    }

    // The row the selection should land on after a delete takes the current one away; consumed by
    // the view's RestoreSelection and cleared there.
    internal long _selectAfterReload;

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

    public void CopySelected()
    {
        if (SelectedEntry() is { } entry)
            _ = ClipboardRuntime.Instance.CopyAsync(entry.Id);
    }

    public void PasteSelected(bool plainText)
    {
        if (view.SearchPending) return;
        var selected = SelectedEntries();
        if (!ClipboardMergeSupport.CanPasteTogether(selected)) return;
        _ = PasteAsync(selected, plainText);
    }

    public void PasteByIndex(int oneBased)
    {
        if (view.SearchPending) return;
        var selectable = ClipboardPanelRows.SelectableIds(view._items);
        if (oneBased < 1 || oneBased > selectable.Count) return;
        PasteEntry(selectable[oneBased - 1]);
    }

    internal void PasteEntry(long id)
    {
        if (view._entryById.TryGetValue(id, out var entry))
            _ = PasteAsync([entry], plainText: false);
    }

    private async Task PasteAsync(IReadOnlyList<ClipboardEntry> entries, bool plainText)
    {
        if (view.SearchPending || view.PastePending) return;
        view.PastePending = true;
        UpdateHintBar();
        try
        {
            // Keep activation until the return window receives focus, so Windows can bring it
            // forward directly instead of selecting another window when this panel closes.
            var paste = ClipboardRuntime.Instance.StartPaste(entries, !plainText, view.PasteTarget);
            if (paste != null && await paste) view.ClosePanel();
        }
        finally
        {
            view.PastePending = false;
            UpdateHintBar();
        }
    }

    // --- row actions ---

    public void DeleteSelected()
    {
        if (view.Lst.SelectedItems.Count != 1 || view.Lst.SelectedItem is not ClipboardPanelItem { IsRow: true } item) return;
        // Remember where the user was: after the reload the deleted id is gone, and without this
        // the selection would jump back to the very top of the list.
        var items = view._items;
        var index = items.IndexOf(item);
        for (var next = index + 1; next < items.Count && _selectAfterReload == 0; next++)
            if (items[next].IsRow) _selectAfterReload = items[next].Id;
        for (var prev = index - 1; prev >= 0 && _selectAfterReload == 0; prev--)
            if (items[prev].IsRow) _selectAfterReload = items[prev].Id;
        ClipboardRuntime.Instance.Delete(item.Id);
    }

    public void TogglePinSelected()
    {
        if (view.Lst.SelectedItems.Count != 1 || view.Lst.SelectedItem is not ClipboardPanelItem { IsRow: true } item) return;
        ClipboardRuntime.Instance.TogglePin(item.Id);
    }

    /// <summary>Toggles a 10-minute capture pause straight from the panel, no settings trip.</summary>
    public void TogglePauseCapture()
    {
        if (ClipboardRuntime.IsCapturePaused())
            ClipboardRuntime.PauseCaptureFor(TimeSpan.Zero);
        else
            ClipboardRuntime.PauseCaptureFor(TimeSpan.FromMinutes(10));
        view.RefreshCaptureStatus();
    }

    /// <summary>PasteBar-style portability: one zip with the database snapshot and every image,
    /// restorable on this machine or a fresh install.</summary>
    public void ExportHistory()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"lertaro-clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            Filter = "ZIP|*.zip",
            DefaultExt = ".zip",
        };
        if (view.RunOwnedDialog(() => dialog.ShowDialog(Window.GetWindow(view))) != true) return;
        _ = ExportAndNotifyAsync(dialog.FileName);
    }

    private async Task ExportAndNotifyAsync(string path)
    {
        if (await ClipboardRuntime.Instance.ExportAsync(path).ConfigureAwait(true))
            PasteBackHelper.Notify("ClipboardHistory_PanelExportDone");
    }

    public async void ImportHistory()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "ZIP|*.zip", DefaultExt = ".zip" };
        if (view.RunOwnedDialog(() => dialog.ShowDialog(Window.GetWindow(view))) != true) return;
        var result = view.RunOwnedDialog(() => PluginMessageBoxService.Show(
            TranslationService.Get("ClipboardHistory_PanelImportConfirm"),
            TranslationService.Get("ClipboardHistory_PluginName"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No));
        if (result != MessageBoxResult.Yes) return;
        if (await ClipboardRuntime.Instance.ImportFromAsync(dialog.FileName).ConfigureAwait(true))
        {
            PasteBackHelper.Notify("ClipboardHistory_PanelImportDone");
        }
    }

    public void ClearHistoryWithConfirm()
    {
        var result = view.RunOwnedDialog(() => PluginMessageBoxService.Show(
            TranslationService.Get("ClipboardHistory_PanelClearConfirm"),
            TranslationService.Get("ClipboardHistory_PluginName"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No));
        if (result != MessageBoxResult.Yes) return;
        ClipboardRuntime.Instance.ClearHistory();
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
        if (view.RunOwnedDialog(() => dialog.ShowDialog(Window.GetWindow(view))) != true) return;
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
        if (!view.IsPreviewShown) return;
        var entry = SelectedEntry();
        FlushNote();
        view.PreviewTitle.Text = entry is null ? string.Empty : ClipboardPanelLabels.Subtitle(entry);
        view.PreviewContext.Text = view.PreviewTitle.Text;
        view.NoteEditor = ClipboardPanelPreview.Apply(view.PreviewContent, view.PreviewTitle, entry, ImagesDirectory(),
            text => { if (entry != null && text != entry.Note) ClipboardRuntime.Instance.SetNote(entry.Id, text); });
        if (view.NoteEditor != null)
            view.NoteEditor.LostKeyboardFocus += (_, _) => view.QueueSnapshotRefresh();
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
        var selected = SelectedEntries();
        var canPaste = ClipboardMergeSupport.CanPasteTogether(selected);
        view.PasteButton.IsEnabled = !view.SearchPending && !view.PastePending && canPaste;
        view.CopyButton.IsEnabled = !view.SearchPending && !view.PastePending && selected.Count == 1;
        view.PreviewToggle.IsEnabled = !view.IsCompactLayout || view.IsCompactPreviewOpen || (!view.SearchPending && selected.Count == 1);
        view.HintBar.Text = selected.Count > 1
            ? canPaste
                ? string.Format(TranslationService.Get("ClipboardHistory_PanelSelectedMerge"), selected.Count)
                : TranslationService.Get("ClipboardHistory_PanelSelectSingleImage")
            : view.IsCompactPreviewOpen ? $"Esc · {ClipboardPanelLabels.BackToHistory()}" : ClipboardPanelLabels.HintBar();
    }

    // --- drag out ---

    public void DragEntry(long id)
    {
        if (view._entryById.TryGetValue(id, out var entry) && BuildDragData(entry) is { } data)
            DragDrop.DoDragDrop(view.Lst, data, DragDropEffects.Copy);
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
