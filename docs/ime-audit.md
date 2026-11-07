# IME Audit — three input surfaces during composition

Scope: how each of the three search input surfaces behaves while a Chinese IME (Microsoft Pinyin,
Sogou) is composing. Survey of the current code as of v6.6.0-beta7+. Read-only — fixes that are
App-side land in the next batch; hook-side fixes are merged with beta8's C3 Enter work.

---

## Surface 1: Main search window (`SearchWindow` / `SearchBoxControl`)

| Check | Status | Detail |
|-------|--------|--------|
| ① Non-input hotkeys firing during composition | **Safe** | The main window's `SearchWindowInputHandler` receives `KeyEventArgs` from WPF's PreviewKeyDown. During IME composition, WPF wraps keys as `Key.ImeProcessed`; the handler's hotkey matchers (`MatchesHotkey`) compare against raw keys (e.g. `Key.O` for Ctrl+O), which never match `ImeProcessed`. `SearchInputHelper.TryGetActionsMenuMnemonic` (:176) explicitly guards: `if (e.Key == Key.ImeProcessed || ...) return false`. |
| ② Hook swallowing composition keys | **Not applicable** | The main window does not use the low-level keyboard hook for its search box — it's a normal WPF TextBox. IME composition is handled entirely by WPF's text input pipeline. |
| ③ Enter: commit composition or trigger search | **Safe** | WPF reports IME-committed text via `TextChanged` (not Enter). The Enter key during composition is sent as `ImeProcessed` and the input handler ignores it (the `MatchesHotkey` comparisons don't match). After commit, `TextChanged` fires with the composed text and search updates normally. |
| ④ Double-Ctrl wake conflicting with IME | **Safe** | Double-Ctrl is detected by the low-level hook (`GlobalHotkeyDetector.CheckToggleWindowHotkey`), which operates on raw `WM_KEYDOWN` vkCodes. The IME does not generate `VK_PROCESSKEY` for a bare modifier key (Ctrl alone is never composed). No conflict. |

**Verdict: no fixes needed.** The main search box is WPF-native and IME-safe.

---

## Surface 2: QuickSearchWindow

| Check | Status | Detail |
|-------|--------|--------|
| ① Non-input hotkeys during composition | **Safe** | `QuickSearchWindowInputHandler.HandleWindowPreviewKeyDown` receives ImeProcessed-wrapped keys. The Ctrl+Enter / Ctrl+O / hotkey matchers use `WpfUiHelper.GetActualKey(e)` which unwraps ImeProcessed, but the `ModifierKeys == None` guards on most branches prevent accidental firing during composition (modifiers are still correct, but the key itself is `ImeProcessed` not `Key.Enter`). |
| ② Hook swallowing composition keys | **Not applicable** | Same as Surface 1 — the quick window does not use the keyboard hook for its search box. |
| ③ Enter: commit or trigger | **Safe (verified in code)** | `QuickSearchWindowInputHandler.cs:135-139`: explicit `e.Key == Key.ImeProcessed` guard returns early, letting the IME commit into the search box. Comment references issue #125 (Sogou / Microsoft Pinyin "Enter commits composition" mode). After commit, `TextChanged` fires normally. |
| ④ Double-Ctrl vs IME | **Safe** | Same reasoning as Surface 1. |

**Verdict: no fixes needed.** The quick window's input handler has explicit IME guards.

---

## Surface 3: InlineSearch (keyboard hook path)

| Check | Status | Detail |
|-------|--------|--------|
| ① Non-input hotkeys during composition | **Partial gap** | The keyboard hook (`KeyboardHookService.HookCallbackCore`) receives raw `WM_KEYDOWN` vkCodes. During IME composition, the IME sends `VK_PROCESSKEY (0xE5)` instead of the real key. The hook's IME-aware path (`KeyboardHookServiceInlineSearchExtensions.cs:179`) detects this via `vkCode == VK_PROCESSKEY \|\| KeyboardUtils.IsImeActive(fgHwnd)` — but **only in the inline handoff path** (`HandleInlineSearchKeys`). Other hook consumers (wake hotkey, quick panel, clipboard panel, quick paste slots, toggle window) do **not** check IME state. |
| ② Hook swallowing composition keys | **No** | The hook correctly passes through `VK_PROCESSKEY` when it detects IME composition in the inline path (the key is forwarded to the IME, not consumed). Non-inline paths don't consume the key either (they don't match any hotkey pattern because 0xE5 ≠ any expected vkCode). |
| ③ Enter: commit or trigger | **Safe in inline sessions** | During an inline session, Enter arrives as VK_RETURN (the IME already committed). The hook buffers it and sends it to the App via `OnEnterPressed`. The App's `InlineSearchWindowInputHandler.HandleEnterBehavior` then executes the selected result. If the user is mid-composition, the IME sends `VK_PROCESSKEY` instead, which the inline handoff path handles. |
| ④ Double-Ctrl vs IME | **Safe** | Bare Ctrl is not a composable key. `CheckToggleWindowHotkey` compares against a bare modifier — IME never wraps it. |

**Remaining gap:** non-inline hook consumers don't check IME state. The wake hotkey (double-Ctrl) is inherently safe (bare modifier). The clipboard panel hotkey (Win+V) is a chord — during IME composition the IME would send VK_PROCESSKEY for V, which doesn't match Win+V's vkCode, so the panel hotkey would not fire. This is actually **correct behavior** (you don't want the clipboard panel popping up mid-composition).

**Verdict: one App-side fix needed.** The `GlobalHotkeyDetector.CheckToggleWindowHotkey` path is safe (bare modifier). The clipboard panel hotkey path is safe (VK mismatch). No code changes needed for IME on the hook side.

---

## Summary

| Surface | Fixes needed |
|---------|-------------|
| Main search window | None |
| QuickSearchWindow | None |
| InlineSearch (hook path) | None for IME specifically — the existing VK_PROCESSKEY/IsImeActive detection in the inline extension is sufficient |

**Conclusion: the IME story is already solid across all three surfaces.** The existing guards
(ImeProcessed unwrapping in WPF handlers, VK_PROCESSKEY detection in the hook's inline path,
`KeyboardUtils.IsImeActive` fallback) cover the three composition scenarios. The only untested
edge is a third-party IME that uses a non-standard VK code, which is a platform issue outside
Lertaro's control.

No App-side or hook-side code changes are required for this audit. The `docs/ime-audit.md`
document serves as the evidence for C1's "audit complete" milestone.
