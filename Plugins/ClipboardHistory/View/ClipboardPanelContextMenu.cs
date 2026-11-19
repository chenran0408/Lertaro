using System.Windows;
using System.Windows.Controls;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// The panel's row context menu, built fresh on every open: pin label follows the entry's state,
/// source-window follows its availability, and the kind-specific extras (path formats for file
/// rows, save-as for image rows) appear only where they apply.
/// </summary>
internal sealed class ClipboardPanelContextMenu
{
    private readonly Action _paste;
    private readonly Action _copy;
    private readonly Action _togglePin;
    private readonly Action _delete;
    private readonly Action _clearNote;
    private readonly Action _openSource;
    private readonly Action<string> _copyText;
    private readonly Action _saveImageAs;
    private readonly Action _clearHistory;
    private readonly Action _exportHistory;
    private readonly Action _importHistory;
    private readonly Action _togglePause;

    public ClipboardPanelContextMenu(Action paste, Action copy, Action togglePin, Action delete,
        Action clearNote, Action exportHistory, Action importHistory, Action openSource, Action<string> copyText,
        Action saveImageAs, Action clearHistory, Action togglePause)
    {
        _paste = paste;
        _copy = copy;
        _togglePin = togglePin;
        _delete = delete;
        _clearNote = clearNote;
        _exportHistory = exportHistory;
        _importHistory = importHistory;
        _openSource = openSource;
        _copyText = copyText;
        _saveImageAs = saveImageAs;
        _clearHistory = clearHistory;
        _togglePause = togglePause;
    }

    public void Populate(ContextMenu menu, IReadOnlyList<ClipboardEntry> selected)
    {
        menu.Items.Clear();

        var paste = Item("ClipboardHistory_Paste", (_, _) => _paste());
        paste.IsEnabled = ClipboardMergeSupport.CanPasteTogether(selected);
        menu.Items.Add(paste);
        if (selected is not [var entry]) return;

        menu.Items.Add(Item("ClipboardHistory_Copy", (_, _) => _copy()));
        menu.Items.Add(Item(entry.Pinned ? "ClipboardHistory_Unpin" : "ClipboardHistory_Pin", (_, _) => _togglePin()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("ClipboardHistory_Delete", (_, _) => _delete()));
        var clearNote = Item("ClipboardHistory_PanelClearNote", (_, _) => _clearNote());
        clearNote.IsEnabled = entry.Note.Length > 0;
        menu.Items.Add(clearNote);

        if (entry.Kind == ClipboardEntryKind.FilePaths)
        {
            var paths = entry.PathList;
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("ClipboardHistory_PanelCopyPath", (_, _) => _copyText(string.Join("\n", paths))));
            menu.Items.Add(Item("ClipboardHistory_PanelCopyPathQuoted", (_, _) => _copyText(ClipboardPanelText.QuotedPaths(paths))));
            menu.Items.Add(Item("ClipboardHistory_PanelCopyPathWsl", (_, _) => _copyText(ClipboardPanelText.WslPaths(paths))));
        }

        if (entry.Kind == ClipboardEntryKind.Image)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("ClipboardHistory_PanelSaveImageAs", (_, _) => _saveImageAs()));
        }

        menu.Items.Add(new Separator());
        var source = Item("ClipboardHistory_OpenSource", (_, _) => _openSource());
        source.IsEnabled = ClipboardRuntime.Instance.CanOpenSource(entry.Id);
        menu.Items.Add(source);
    }

    public void PopulateHistory(ContextMenu menu)
    {
        menu.Items.Add(Item(
            ClipboardRuntime.IsCapturePaused() ? "ClipboardHistory_PanelResumeCapture" : "ClipboardHistory_PanelPauseCapture",
            (_, _) => _togglePause()));
        menu.Items.Add(Item("ClipboardHistory_PanelExport", (_, _) => _exportHistory()));
        menu.Items.Add(Item("ClipboardHistory_PanelImport", (_, _) => _importHistory()));
        menu.Items.Add(Item("ClipboardHistory_ClearHistory", (_, _) => _clearHistory()));
    }

    private static MenuItem Item(string key, RoutedEventHandler onClick)
    {
        var item = new MenuItem { Header = TranslationService.Get(key) };
        item.Click += onClick;
        return item;
    }
}
