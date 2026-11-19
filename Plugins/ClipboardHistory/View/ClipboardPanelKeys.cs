using System.Windows.Input;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>What the panel does in response to one key press.</summary>
internal enum ClipboardPanelAction
{
    None,
    MoveUp,
    MoveDown,
    MoveTop,
    MoveBottom,
    ExtendUp,
    ExtendDown,
    SelectAll,
    PasteSelected,
    PasteSelectedPlainText,
    DeleteSelected,
    TogglePinSelected,
    ClearSearch,
    Close,
    FocusSearch,
    PasteByIndex,
}

/// <summary>The action plus, for <see cref="ClipboardPanelAction.PasteByIndex"/>, the 1-based
/// index of the row to paste (the digit the user pressed).</summary>
internal readonly record struct ClipboardPanelKeyResult(ClipboardPanelAction Action, int Index)
{
    public static implicit operator ClipboardPanelKeyResult(ClipboardPanelAction action) => new(action, 0);
}

internal enum ClipboardPanelFocus
{
    Search,
    List,
    Other,
}

internal static class ClipboardPanelKeys
{
    public static ClipboardPanelKeyResult Resolve(Key key, ModifierKeys modifiers, ClipboardPanelFocus focus, bool searchHasText)
    {
        if (key == Key.Escape && modifiers == ModifierKeys.None)
            return searchHasText && focus != ClipboardPanelFocus.Other ? ClipboardPanelAction.ClearSearch : ClipboardPanelAction.Close;
        if (key == Key.F && modifiers == ModifierKeys.Control)
            return ClipboardPanelAction.FocusSearch;

        // The preview and note editor keep their native text editing and keyboard behavior.
        if (focus == ClipboardPanelFocus.Other)
            return ClipboardPanelAction.None;

        if (modifiers == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.Up: return ClipboardPanelAction.MoveUp;
                case Key.Down: return ClipboardPanelAction.MoveDown;
                case Key.Enter: return ClipboardPanelAction.PasteSelected;
                case Key.Home when focus == ClipboardPanelFocus.List: return ClipboardPanelAction.MoveTop;
                case Key.End when focus == ClipboardPanelFocus.List: return ClipboardPanelAction.MoveBottom;
                case Key.Delete when focus == ClipboardPanelFocus.List: return ClipboardPanelAction.DeleteSelected;
            }
        }
        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.Enter: return ClipboardPanelAction.PasteSelectedPlainText;
                case Key.A when focus == ClipboardPanelFocus.List: return ClipboardPanelAction.SelectAll;
                case Key.P when focus == ClipboardPanelFocus.List: return ClipboardPanelAction.TogglePinSelected;
            }
        }
        if (modifiers == ModifierKeys.Shift && focus == ClipboardPanelFocus.List)
        {
            if (key == Key.Up) return ClipboardPanelAction.ExtendUp;
            if (key == Key.Down) return ClipboardPanelAction.ExtendDown;
        }

        var slot = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
            _ => 0,
        };
        return slot > 0 && (modifiers == ModifierKeys.Control || (modifiers == ModifierKeys.None && focus == ClipboardPanelFocus.List))
            ? new ClipboardPanelKeyResult(ClipboardPanelAction.PasteByIndex, slot)
            : ClipboardPanelAction.None;
    }
}
