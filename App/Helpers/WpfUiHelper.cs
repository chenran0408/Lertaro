using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.CompilerServices;

namespace Lertaro.App.Helpers;

public static class WpfUiHelper
{
    private sealed class TapState { public int Key; public uint Time; }
    private static readonly Dictionary<string, TapState> DoubleTapStates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<System.Windows.Input.KeyEventArgs, Dictionary<string, bool>> EventMatches = new();
    public static ModifierKeys GetWpfModifier(string modifierStr)
    {
        if (string.IsNullOrEmpty(modifierStr)) return ModifierKeys.Control;
        return modifierStr.Trim().ToUpperInvariant() switch
        {
            "ALT" => ModifierKeys.Alt,
            "SHIFT" => ModifierKeys.Shift,
            "WIN" or "WINDOWS" => ModifierKeys.Windows,
            "NONE" => ModifierKeys.None,
            _ => ModifierKeys.Control,
        };
    }

    /// <summary>
    /// Unwraps WPF's two "the real key is somewhere else" cases: Alt held sets e.Key = Key.System with
    /// e.SystemKey holding the actual key, and an active IME (even just intercepting a plain ASCII key,
    /// not necessarily composing anything) sets e.Key = Key.ImeProcessed with e.ImeProcessedKey holding
    /// it instead -- without this, callers see the literal placeholder "ImeProcessed"/"System" key
    /// rather than the key that was actually pressed.
    /// </summary>
    public static Key GetActualKey(System.Windows.Input.KeyEventArgs e) => e.Key switch
    {
        Key.System => e.SystemKey,
        Key.ImeProcessed => e.ImeProcessedKey,
        _ => e.Key
    };

    /// <summary>Parses a recorder-style combo string (e.g. "Ctrl+Shift+Enter") into its key + modifiers.</summary>
    public static bool TryParseHotkey(string? hotkey, out Key key, out ModifierKeys modifiers)
    {
        key = Key.None;
        modifiers = ModifierKeys.None;
        if (string.IsNullOrWhiteSpace(hotkey)) return false;
        if (Core.HotkeyStringFormat.IsDoubleTap(hotkey, out var tappedKey))
        {
            if (Enum.TryParse(tappedKey, true, out key)) return true;
            key = Key.None;
            return false;
        }

        foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = part.Trim().ToUpperInvariant();
            switch (clean)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (!Enum.TryParse(clean, true, out key) && clean.Length == 1 && char.IsDigit(clean[0]))
                        Enum.TryParse("D" + clean, true, out key);
                    break;
            }
        }

        return key != Key.None;
    }

    /// <summary>Whether the currently-held modifiers + key match a stored recorder-style combo string.</summary>
    public static bool MatchesHotkey(string? hotkey, ModifierKeys currentModifiers, Key currentKey) =>
        !Core.HotkeyStringFormat.IsDoubleTap(hotkey, out _)
        && TryParseHotkey(hotkey, out var key, out var modifiers) && key == currentKey && modifiers == currentModifiers;

    /// <summary>Matches a recorder binding against a WPF key event, including explicit double taps.</summary>
    public static bool MatchesHotkey(string? hotkey, System.Windows.Input.KeyEventArgs e)
    {
        var key = GetActualKey(e);
        foreach (var (configuredHotkey, existingState) in DoubleTapStates.ToArray())
        {
            if (existingState.Key != (int)key || Keyboard.Modifiers != ModifierKeys.None)
                DoubleTapStates.Remove(configuredHotkey);
        }
        if (!Core.HotkeyStringFormat.IsDoubleTap(hotkey, out var tappedKey))
            return MatchesHotkey(hotkey, Keyboard.Modifiers, key);
        if (Keyboard.Modifiers != ModifierKeys.None
            || !Enum.TryParse<Key>(tappedKey, true, out var expected) || key != expected) return false;

        var eventResults = EventMatches.GetOrCreateValue(e);
        if (eventResults.TryGetValue(hotkey!, out var cached)) return cached;
        if (e.IsRepeat) { eventResults[hotkey!] = false; return false; }

        var now = unchecked((uint)e.Timestamp);
        var triggered = false;
        if (DoubleTapStates.TryGetValue(hotkey!, out var state)
            && state.Key == (int)key && unchecked(now - state.Time) <= 320)
        {
            DoubleTapStates.Remove(hotkey!);
            triggered = true;
        }
        else DoubleTapStates[hotkey!] = new TapState { Key = (int)key, Time = now };

        eventResults[hotkey!] = triggered;
        return triggered;
    }


    public static ScrollViewer? GetScrollViewer(DependencyObject depObj)
    {
        if (depObj is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            var result = GetScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// Converts a ScrollViewer's pixel-based VerticalOffset (ScrollViewer.CanContentScroll="False") back
    /// to an item index -- the shortcut-hint helpers (QuickSearchShortcutHelper, InlineSearchShortcutHelper)
    /// need "the first visible row's index" to know where to resume the Ctrl+1..9 labeling, which used to
    /// be exactly what VerticalOffset was while the ListBox scrolled by item (the WPF default).
    /// </summary>
    public static int GetFirstVisibleIndexFromPixelOffset(double verticalOffset, double rowHeight) =>
        rowHeight > 0 ? (int)Math.Floor(verticalOffset / rowHeight) : 0;

    /// <summary>
    /// Same purpose as GetFirstVisibleIndexFromPixelOffset, but reads the ScrollViewer's OWN current
    /// CanContentScroll instead of assuming a fixed scrolling mode -- a caller that assumed one mode
    /// unconditionally would silently read the wrong unit the moment the other mode is active.
    /// </summary>
    public static int GetFirstVisibleIndex(ScrollViewer? scrollViewer, double rowHeight)
    {
        if (scrollViewer == null)
            return 0;
        return scrollViewer.CanContentScroll
            ? (int)Math.Round(scrollViewer.VerticalOffset)
            : GetFirstVisibleIndexFromPixelOffset(scrollViewer.VerticalOffset, rowHeight);
    }
}
