using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using Lertaro.Core;
using Lertaro.App.Services;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.Services.Favorites;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using Lertaro.App.Services.ShellMenu.Presenter;
namespace Lertaro.App.Helpers;

public static class SearchInputHelper
{
    private sealed class PendingDoubleTapAction
    {
        internal Key Key;
        internal uint Time;
        internal string SearchText = string.Empty;
        internal int SelectionStart;
        internal int SelectionLength;
        internal AppSearchResult[] Results = [];
        internal Action? DeferredBehavior;
        internal DispatcherTimer? Timer;
    }

    private sealed class PendingDoubleTapActionState
    {
        internal PendingDoubleTapAction? Value;
    }

    private static readonly ConditionalWeakTable<ISearchWindow, PendingDoubleTapActionState> PendingDoubleTapActions = new();
    private const uint DoubleTapWindowMilliseconds = 320;

    public static bool IsQuickLookKey(System.Windows.Input.KeyEventArgs e)
    {
        return WpfUiHelper.MatchesHotkey(UserSettings.Load().Hotkeys.QuickLookHotkey, e);
    }

    // Shared by SearchWindow/QuickSearchWindow/InlineSearchWindow's own Right-arrow-opens-Actions check,
    // so it only fires when Right would otherwise be a no-op for the caret (not moving it or collapsing a
    // selection) instead of hijacking normal text-cursor movement while editing earlier in the query.
    public static bool IsSearchCaretAtEnd(ISearchWindow window) => window.SearchTextBox.IsKeyboardFocusWithin
        && window.SearchTextBox.SelectionLength == 0
        && window.SearchTextBox.CaretIndex >= window.SearchTextBox.Text.Length;

    // Shared by both search windows for the "keep search box content" setting: with it on, the text a
    // window opens with is selected whole, so the first keystroke replaces it. The quick window applies
    // this to what FinishHide kept, and the full window to the query it was handed (from "show more", a
    // plugin, or a repeat summon). With the setting off the caret keeps its old behaviour -- parked after
    // the text in the full window, so typing still appends instead of replacing.
    public static bool ShouldSelectCarriedText(string? carriedText, bool keepSearchText) =>
        keepSearchText && !string.IsNullOrEmpty(carriedText);

    public static bool HandleActionsModeKeys(System.Windows.Input.KeyEventArgs e, ISearchWindow? window, ShellMenuPresenter? menuPresenter)
    {
        if (menuPresenter == null || !menuPresenter.IsInActionsMode)
            return false;

        // Read once up front: the custom next/previous-item hotkeys must win over the hardcoded bare-Tab
        // shortcut below whenever the user has bound one of them to Tab, otherwise a Tab-as-next-item
        // binding would silently be swallowed by "Tab enters submenu" and never reach the match further down.
        var settings = UserSettings.Load().Hotkeys;
        var isNextItemHotkey = WpfUiHelper.MatchesHotkey(settings.NextItemHotkey, e);
        var isPreviousItemHotkey = WpfUiHelper.MatchesHotkey(settings.PreviousItemHotkey, e);

        // Every bare key check below (including Escape) requires no modifiers -- otherwise it would
        // shadow a user-configurable combo hotkey that happens to share the same base key (e.g. the
        // Startup Panel's default Ctrl+Left/Right, or a plugin action's Ctrl+Enter) before it ever
        // reaches the handling further down (or falls through to the window's own hotkey dispatch).
        // Nothing is bound to a modified Escape today, but guarding it anyway costs nothing and rules
        // the whole bug class out here rather than leaving one exception someone has to remember.
        var noModifiers = Keyboard.Modifiers == ModifierKeys.None;
        var actionSearchBox = window?.UsesFloatingActionsMenu == true ? window.ActionsSearchTextBox : window?.SearchTextBox;

        if (menuPresenter.TryExecuteActiveHotkey(e))
        {
            // Persistent hosts keep their window open after an action, but their floating actions panel
            // must close just like the clicked-action path. Dismissible hosts already hide themselves
            // inside HotkeyActionTrigger, so do not run their exit transition twice.
            if (window?.KeepWindowOpenAfterActionsHotkey == true)
                menuPresenter.ExitActionsMode();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Escape && noModifiers)
        {
            HandleActionsEscape(window, menuPresenter);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Left && noModifiers)
        {
            menuPresenter.GoBackMenuOrExit();
            e.Handled = true;
            return true;
        }

        if ((e.Key == Key.Right && noModifiers) || (e.Key == Key.Tab && noModifiers && !isNextItemHotkey && !isPreviousItemHotkey))
        {
            menuPresenter.EnterSubMenu();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Down && noModifiers)
        {
            menuPresenter.NavigateActionsList(1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Up && noModifiers)
        {
            menuPresenter.NavigateActionsList(-1);
            e.Handled = true;
            return true;
        }

        // The results list also accepts the user's configurable next/previous-item hotkeys (not just the
        // literal arrow keys above); the actions list should match so a custom binding still works once
        // the menu is open instead of silently falling through to move the hidden results-list selection.
        if (isNextItemHotkey)
        {
            menuPresenter.NavigateActionsList(1);
            e.Handled = true;
            return true;
        }
        if (isPreviousItemHotkey)
        {
            menuPresenter.NavigateActionsList(-1);
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Enter && noModifiers)
        {
            menuPresenter.ExecuteSelectedAction();
            e.Handled = true;
            return true;
        }

        // Mnemonic letters (a provider writes the letter as the item's ShortcutHint). Only while the
        // actions filter box is empty: once the user starts typing a filter, letters belong to the filter
        // -- and typing into it is the only way to narrow a long menu down, so a mnemonic must not steal
        // the first keystroke of it. Selecting the item and running the ordinary selected-action path
        // keeps one execution code path for click, Enter and letter.
        if (window != null && TryGetActionsMenuMnemonic(e, Keyboard.Modifiers, actionSearchBox?.Text,
                window.LstActions.Items.OfType<ActionMenuItem>(), out var shortcutItem))
        {
            window.LstActions.SelectedItem = shortcutItem;
            menuPresenter.ExecuteSelectedAction();
            e.Handled = true;
            return true;
        }

        if (e.Key == Key.Back && noModifiers)
        {
            if (actionSearchBox != null && string.IsNullOrEmpty(actionSearchBox.Text))
            {
                menuPresenter.GoBackMenuOrExit();
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    internal static bool TryGetActionsMenuMnemonic(System.Windows.Input.KeyEventArgs e, ModifierKeys modifiers, string? filterText,
        IEnumerable<ActionMenuItem> items, out ActionMenuItem? shortcutItem)
    {
        shortcutItem = null;
        // An IME can wrap a plain ASCII key as ImeProcessed, including the first key that starts
        // composition. Let text input deliver it to the filter instead of treating it as a mnemonic.
        // Other key routing (including modifier hotkeys above) still uses GetActualKey.
        if (e.Key == Key.ImeProcessed || modifiers != ModifierKeys.None || !string.IsNullOrEmpty(filterText))
            return false;

        shortcutItem = ActionsMenuShortcutMatcher.Find(items, LetterOf(WpfUiHelper.GetActualKey(e)));
        return shortcutItem != null;
    }

    // Keep every actions-menu Escape entry point identical, including mouse right-click and the
    // inline window's global keyboard hook. A non-empty action filter is cleared first; only the next
    // Escape-equivalent input navigates back or exits the actions menu.
    public static bool HandleActionsEscape(ISearchWindow? window, ShellMenuPresenter menuPresenter)
    {
        var actionSearchBox = window?.UsesFloatingActionsMenu == true ? window.ActionsSearchTextBox : window?.SearchTextBox;
        if (actionSearchBox != null && !string.IsNullOrEmpty(actionSearchBox.Text))
        {
            actionSearchBox.Clear();
            return true;
        }

        menuPresenter.GoBackMenuOrExit();
        return true;
    }

    /// <summary>
    /// Fires an action hotkey (e.g. Ctrl+C copy, Ctrl+Enter locate) on the selected result without
    /// opening any menu — the always-available behavior the quick window has. Only runs when a modifier
    /// is held and the actions menu is allowed for the selection, so plain typing pays no cost and
    /// suppressed rows (apps / plugin results / ...) suppress the hotkeys too. A bare key (no modifier)
    /// is exempt from that requirement when it's actually bound to something (checked against the real
    /// configured hotkeys, default or user-overridden, via HotkeyActionTrigger.HasBareKeyActionHotkey
    /// -- not hardcoded to e.g. Delete, so rebinding DeleteFileAction to a different bare key still
    /// works here) and, like the Right-arrow-opens-Actions check below, only when the caret is already
    /// at the end of the query -- otherwise the key keeps its normal text-editing meaning while editing
    /// earlier in the text. A non-empty selection is also always left to the search box, so standard
    /// text commands such as Ctrl+C, Ctrl+V, and Ctrl+X are not mistaken for result actions.
    /// </summary>
    public static bool TryActionHotkey(System.Windows.Input.KeyEventArgs e, ISearchWindow window, ShellMenuPresenter? menuPresenter)
    {
        if (window.SearchTextBox.IsKeyboardFocusWithin && window.SearchTextBox.SelectionLength > 0)
            return false;

        var hotkeys = Keyboard.Modifiers == ModifierKeys.None && IsSearchCaretAtEnd(window) ? UserSettings.Load() : null;
        var bareKeyBound = hotkeys != null
            && HotkeyActionTrigger.HasBareKeyActionHotkey(WpfUiHelper.GetActualKey(e), hotkeys);
        if ((Keyboard.Modifiers != ModifierKeys.None || bareKeyBound)
            && window.LstResults.SelectedItem is AppSearchResult selectedResult
            && menuPresenter != null
            && menuPresenter.CanShowActionsMenu(new[] { selectedResult }))
        {
            if (hotkeys != null) RememberDoubleTapActionInput(e, window, selectedResult, hotkeys);
            if (HotkeyActionTrigger.TryExecute(e, selectedResult, window))
            {
                e.Handled = true;
                return true;
            }
        }
        return false;
    }

    public static bool HandleCommonSearchKeys(System.Windows.Input.KeyEventArgs e, ISearchWindow window, ShellMenuPresenter? menuPresenter,
        Action? deferredEnterBehavior = null)
    {
        // Let the first printable tap behave as text. If the second tap completes a plugin action,
        // restore the original query and selection immediately before dispatching against that result.
        if (TryExecutePendingDoubleTapAction(e, window, menuPresenter))
            return true;

        if (TryDeferDoubleTapEnter(e, window, menuPresenter, deferredEnterBehavior))
            return true;

        // 1. Actions Mode keys
        if (HandleActionsModeKeys(e, window, menuPresenter))
            return true;

        // 1b. Action hotkeys on the selected item (Ctrl+C copy, Ctrl+Enter locate, ...).
        if (TryActionHotkey(e, window, menuPresenter))
            return true;

        // 2. QuickLook
        if (window.GetType().Name != "InlineSearchWindow" && IsQuickLookKey(e))
        {
            if (window.LstResults.SelectedItem is AppSearchResult result && result.CanPreview)
            {
                QuickLookManager.Instance.Toggle((Window)window, result.FullPath);
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    private static void RememberDoubleTapActionInput(System.Windows.Input.KeyEventArgs e, ISearchWindow window,
        AppSearchResult selectedResult, UserSettings settings)
    {
        var state = PendingDoubleTapActions.GetValue(window, static _ => new PendingDoubleTapActionState());
        var key = WpfUiHelper.GetActualKey(e);
        if (e.IsRepeat) return;
        if (Keyboard.Modifiers != ModifierKeys.None
            || !HotkeyActionTrigger.HasDoubleTapBareKeyActionHotkey(key, settings))
        {
            state.Value = null;
            return;
        }

        var results = window.LstResults.SelectedItems.OfType<AppSearchResult>().ToArray();
        if (results.Length == 0) results = [selectedResult];
        state.Value = new PendingDoubleTapAction
        {
            Key = key,
            Time = unchecked((uint)Environment.TickCount),
            SearchText = window.SearchTextBox.Text,
            SelectionStart = window.SearchTextBox.SelectionStart,
            SelectionLength = window.SearchTextBox.SelectionLength,
            Results = results
        };
    }

    private static bool TryExecutePendingDoubleTapAction(System.Windows.Input.KeyEventArgs e, ISearchWindow window,
        ShellMenuPresenter? menuPresenter)
    {
        if (!PendingDoubleTapActions.TryGetValue(window, out var state) || state.Value is not { } pending)
            return false;

        var key = WpfUiHelper.GetActualKey(e);
        if (e.IsRepeat && key == pending.Key && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (pending.DeferredBehavior != null)
            {
                e.Handled = true;
                return true;
            }
            return false;
        }
        var isSecondTap = IsSecondTap(key, e.IsRepeat, Keyboard.Modifiers, unchecked((uint)Environment.TickCount),
            pending.Key, pending.Time);
        if (!isSecondTap)
        {
            ClearPending(state, runDeferredBehavior: true);
            return false;
        }

        ClearPending(state, runDeferredBehavior: false);

        var executed = ExecuteDeferredAction(
            () => HotkeyActionTrigger.TryExecute(e, pending.Results, window, () => RestoreSearchInput(window, pending)),
            pending.DeferredBehavior);
        if (executed && window.KeepWindowOpenAfterActionsHotkey && menuPresenter?.IsInActionsMode == true)
            menuPresenter.ExitActionsMode();

        // Do not let a cached hotkey match fall through to a second route using a filtered selection.
        e.Handled = true;
        return true;
    }

    private static bool TryDeferDoubleTapEnter(System.Windows.Input.KeyEventArgs e, ISearchWindow window,
        ShellMenuPresenter? menuPresenter, Action? deferredBehavior)
    {
        if (deferredBehavior == null || e.Key == Key.ImeProcessed || e.IsRepeat || WpfUiHelper.GetActualKey(e) != Key.Enter
            || Keyboard.Modifiers != ModifierKeys.None)
            return false;

        var settings = UserSettings.Load();
        if (!HotkeyActionTrigger.HasDoubleTapBareKeyActionHotkey(Key.Enter, settings))
            return false;

        var selected = window.LstResults.SelectedItem as AppSearchResult;
        if (selected == null || menuPresenter?.CanShowActionsMenu(new[] { selected }) != true)
            return false;

        var state = PendingDoubleTapActions.GetValue(window, static _ => new PendingDoubleTapActionState());
        ClearPending(state, runDeferredBehavior: false);
        var results = window.LstResults.SelectedItems.OfType<AppSearchResult>().ToArray();
        if (results.Length == 0) results = [selected];
        var pending = new PendingDoubleTapAction
        {
            Key = Key.Enter,
            Time = unchecked((uint)Environment.TickCount),
            SearchText = window.SearchTextBox.Text,
            SelectionStart = window.SearchTextBox.SelectionStart,
            SelectionLength = window.SearchTextBox.SelectionLength,
            Results = results,
            DeferredBehavior = deferredBehavior
        };
        state.Value = pending;
        ArmDoubleTapActionMatchers(e, FavoriteHotkeyConflicts.PluginHotkeys(settings));
        var timer = new DispatcherTimer(DispatcherPriority.Input, window.SearchTextBox.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(DoubleTapWindowMilliseconds)
        };
        pending.Timer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(state.Value, pending))
                ClearPending(state, runDeferredBehavior: true);
        };
        timer.Start();
        e.Handled = true;
        return true;
    }

    private static void ClearPending(PendingDoubleTapActionState state, bool runDeferredBehavior)
    {
        var pending = state.Value;
        state.Value = null;
        pending?.Timer?.Stop();
        if (runDeferredBehavior)
            pending?.DeferredBehavior?.Invoke();
    }

    internal static bool IsSecondTap(Key key, bool isRepeat, ModifierKeys modifiers, uint now, Key pendingKey, uint pendingTime) =>
        !isRepeat && modifiers == ModifierKeys.None && key == pendingKey
        && unchecked(now - pendingTime) <= DoubleTapWindowMilliseconds;

    internal static void ArmDoubleTapActionMatchers(System.Windows.Input.KeyEventArgs e, IEnumerable<string> hotkeys)
    {
        var key = WpfUiHelper.GetActualKey(e);
        foreach (var hotkey in hotkeys)
        {
            if (!HotkeyStringFormat.IsDoubleTap(hotkey, out var tapKey)
                || !Enum.TryParse<Key>(tapKey, true, out var configuredKey)
                || configuredKey != key)
                continue;

            WpfUiHelper.MatchesHotkey(hotkey, e);
        }
    }

    internal static bool ExecuteDeferredAction(Func<bool> executeAction, Action? deferredBehavior)
    {
        if (executeAction()) return true;
        deferredBehavior?.Invoke();
        return false;
    }

    private static void RestoreSearchInput(ISearchWindow window, PendingDoubleTapAction pending)
    {
        var textBox = window.SearchTextBox;
        if (textBox.Text != pending.SearchText) textBox.Text = pending.SearchText;
        var selectionStart = Math.Clamp(pending.SelectionStart, 0, textBox.Text.Length);
        var selectionLength = Math.Clamp(pending.SelectionLength, 0, textBox.Text.Length - selectionStart);
        textBox.Select(selectionStart, selectionLength);

        var list = window.LstResults;
        foreach (var item in list.SelectedItems.Cast<object>().ToArray())
            list.SelectedItems.Remove(item);
        foreach (var result in pending.Results)
            if (list.Items.Contains(result)) list.SelectedItems.Add(result);
    }

    /// <summary>
    /// The letter a key press stands for, as the uppercase form the shortcut hints are compared against,
    /// or <c>'\0'</c> for every key that is not A-Z (so no hint can match it).
    /// </summary>
    private static char LetterOf(Key key) => key >= Key.A && key <= Key.Z ? (char)('A' + (key - Key.A)) : '\0';
}
