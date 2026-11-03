using System.Windows.Input;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>What the panel does in response to one key press.</summary>
internal enum ClipboardPanelAction
{
    None,
    MoveUp,
    MoveDown,
    PasteSelected,
    PasteSelectedPlainText,
    DeleteSelected,
    TogglePinSelected,
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

/// <summary>
/// The panel's full keyboard surface, resolved as a pure function so every binding is unit-testable.
/// Digits paste the Nth visible row only while focus is OUT of the search box: in the box they type,
/// which is why search focus is an input here and not an implementation detail.
/// </summary>
internal static class ClipboardPanelKeys
{
    public static ClipboardPanelKeyResult Resolve(Key key, ModifierKeys modifiers, bool searchFocused)
    {
        switch (key)
        {
            case Key.Escape:
                return ClipboardPanelAction.Close;
            case Key.Up when !searchFocused:
                return ClipboardPanelAction.MoveUp;
            case Key.Down when !searchFocused:
                return ClipboardPanelAction.MoveDown;
            case Key.Enter:
                return modifiers == ModifierKeys.Control
                    ? ClipboardPanelAction.PasteSelectedPlainText
                    : ClipboardPanelAction.PasteSelected;
            case Key.Delete when !searchFocused:
                return ClipboardPanelAction.DeleteSelected;
            case Key.P when modifiers == ModifierKeys.Control && !searchFocused:
                return ClipboardPanelAction.TogglePinSelected;
            case Key.F when modifiers == ModifierKeys.Control:
                return ClipboardPanelAction.FocusSearch;
        }

        if (!searchFocused && key is Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6 or Key.D7 or Key.D8 or Key.D9)
            return new ClipboardPanelKeyResult(ClipboardPanelAction.PasteByIndex, key - Key.D1 + 1);

        return ClipboardPanelAction.None;
    }
}
