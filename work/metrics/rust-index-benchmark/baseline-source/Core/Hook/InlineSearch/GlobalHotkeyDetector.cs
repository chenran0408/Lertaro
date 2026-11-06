namespace Lertaro.Core.Hook.InlineSearch;

public sealed class GlobalHotkeyDetector
{
    private readonly UserSettings _settings;
    private readonly ExplorerTracker _explorerTracker;

    private readonly ModifierDoubleTapDetector _toggleWindowTapDetector = new();
    private readonly ModifierDoubleTapDetector _quickSwitchTapDetector = new();
    private readonly ModifierKeyState _modifierKeyState = new();
    private readonly Dictionary<string, DoubleTapKeyDetector> _doubleTapDetectors = new(StringComparer.OrdinalIgnoreCase);
    private readonly (int OwnerIndex, string Hotkey)[] _favoriteDoubleTapHotkeys;
    private HashSet<int>? _eligibleFavoriteDoubleTapOwners;

    /// <summary>
    /// Where a recognized Quick Switch is handed off. The hook cannot do the work itself: deciding needs the
    /// tracker lock, which the WinEvent thread holds across cross-process reads, and the navigation calls into
    /// another process. Production posts a thread message to the tracker thread (see HookProcess).
    /// </summary>
    private readonly Action<IntPtr> _requestQuickSwitch;

    public GlobalHotkeyDetector(UserSettings settings, ExplorerTracker explorerTracker, Action<IntPtr>? requestQuickSwitch = null)
    {
        _settings = settings;
        _explorerTracker = explorerTracker;
        _requestQuickSwitch = requestQuickSwitch ?? (hwnd => QuickSwitchNavigator.Execute(_explorerTracker, hwnd));
        _favoriteDoubleTapHotkeys = BuildEligibleFavoriteDoubleTaps(settings);
    }

    private readonly HashSet<int> _pressedKeys = new();
    private bool _repeated;
    internal bool QuickPanelKeyRepeated => _repeated;
    internal string? MatchedQuickPanelRuleId { get; private set; }
    private string? _cachedKey, _cachedAlternate;
    private List<QuickPanelWakeBinding>? _cachedBindings;
    private QuickPanelWakeBinding[] _keyRules = [];

    public void OnKeyDown(int vkCode)
    {
        _repeated = !_pressedKeys.Add(vkCode);
        _modifierKeyState.OnKeyDown(vkCode);
        if (!_repeated)
        {
            foreach (var detector in _doubleTapDetectors.Values)
                detector.ResetOnOtherKey(vkCode);
        }
    }

    // Secure-desktop transitions can hide a modifier key-up from the low-level hook; refresh the
    // hook-owned snapshot before processing the next key event.
    internal void SynchronizeModifierState() => _modifierKeyState.Synchronize(vkCode =>
        (KeyboardNativeMethods.GetAsyncKeyState(vkCode) & 0x8000) != 0);

    internal bool HasControlAltOrWindowsDown => _modifierKeyState.HasControlAltOrWindowsDown;

    internal bool CheckModifiersMatch(string expectedModifier) =>
        KeyboardUtils.CheckModifiersMatch(expectedModifier, _modifierKeyState, "NONE");

    internal bool CheckModifiersMatchOnly(string expectedModifier) =>
        KeyboardUtils.CheckModifiersMatch(expectedModifier, _modifierKeyState, "CONTROL");

    /// <summary>Call on WM_KEYUP / WM_SYSKEYUP to reset the "was released" flags.</summary>
    public void OnKeyUp(int vkCode)
    {
        _pressedKeys.Remove(vkCode);
        _modifierKeyState.OnKeyUp(vkCode);
        foreach (var detector in _doubleTapDetectors.Values) detector.OnKeyUp(vkCode);
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out var toggleModifier) &&
            KeyboardUtils.IsModifierKey(vkCode, toggleModifier))
        {
            _toggleWindowTapDetector.OnModifierKeyUp();
        }

        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out var quickSwitchModifier) &&
            KeyboardUtils.IsModifierKey(vkCode, quickSwitchModifier))
        {
            _quickSwitchTapDetector.OnModifierKeyUp();
        }
    }

    public bool CheckToggleWindowHotkey(int vkCode, uint time, out bool consumeKey, Action? onDoubleCtrl)
    {
        consumeKey = false;
        var triggered = false;
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out var clickModifier))
        {
            if (KeyboardUtils.IsModifierKey(vkCode, clickModifier))
            {
                triggered = _toggleWindowTapDetector.OnModifierKeyDown(vkCode, time);
            }
            else
            {
                _toggleWindowTapDetector.ResetOnOtherKey();
            }
        }
        else if (HotkeyStringFormat.IsDoubleTap(_settings.Hotkeys.ToggleWindowHotkey, out _))
        {
            triggered = MatchConfiguredHotkey(_settings.Hotkeys.ToggleWindowHotkey, vkCode, time);
            consumeKey = triggered;
        }
        else
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (CheckModifiersMatch(modifier))
                {
                    triggered = true;
                    consumeKey = true;
                }
            }
        }

        if (triggered)
        {
            onDoubleCtrl?.Invoke();
        }
        return triggered;
    }

    /// <summary>
    /// Whether this keystroke is the toggle hotkey's chord form -- the same test the combo branch of
    /// <see cref="CheckToggleWindowHotkey"/> applies, answered without touching the tap detectors'
    /// state. False for the bare-modifier form, which no chord with a key of its own can be.
    /// </summary>
    internal bool IsToggleWindowComboDown(int vkCode)
    {
        if (HotkeyStringFormat.IsDoubleTap(_settings.Hotkeys.ToggleWindowHotkey, out var doubleTapKey))
            return KeyboardUtils.GetKeyVirtualCode(doubleTapKey) == vkCode && CheckModifiersMatch(string.Empty);
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out _))
            return false;

        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        return targetVk != 0 && vkCode == targetVk && CheckModifiersMatch(modifier);
    }

    /// <summary>Whether this keystroke is the quick panel's own combination, answered without consuming it.</summary>
    internal bool IsQuickPanelComboDown(int vkCode)
    {
        EnsureQuickPanelRules();
        return _keyRules.Any(rule => ConfiguredKeyMatches(rule.Key, vkCode));
    }

    internal bool MatchesWakeTest(string combo, int vkCode, uint time) => MatchConfiguredHotkey(combo, vkCode, time);

    private QuickPanelWakeBinding? FindQuickPanelRule(int vkCode, uint time = 0)
    {
        EnsureQuickPanelRules();
        return _keyRules.FirstOrDefault(r => MatchConfiguredHotkey(r.Key, vkCode, time));
    }

    private void EnsureQuickPanelRules()
    {
        var settings = _settings.Hotkeys;
        if (!ReferenceEquals(_cachedBindings, settings.QuickPanelWakeBindings)
            || _cachedKey != settings.QuickPanelHotkey || _cachedAlternate != settings.QuickPanelAlternateHotkey)
        {
            _cachedBindings = settings.QuickPanelWakeBindings;
            _cachedKey = settings.QuickPanelHotkey; _cachedAlternate = settings.QuickPanelAlternateHotkey;
            _keyRules = QuickPanelWakeBindings.Get(settings).Where(r => r != null && r.Enabled
                && r.Kind == QuickPanelWakeKind.Keyboard && QuickPanelWakeBindings.Validate(r) == null).ToArray();
        }
    }

    private bool MatchConfiguredHotkey(string combo, int vkCode, uint time)
    {
        if (HotkeyStringFormat.IsDoubleTap(combo, out var tappedKey))
        {
            var tappedVk = KeyboardUtils.GetKeyVirtualCode(tappedKey);
            if (tappedVk == 0 || vkCode != tappedVk || !CheckModifiersMatch(string.Empty)) return false;
            if (!_doubleTapDetectors.TryGetValue(combo, out var detector))
                _doubleTapDetectors[combo] = detector = new DoubleTapKeyDetector();
            return detector.OnKeyDown(vkCode, time, _repeated);
        }
        HotkeyStringFormat.ParseCombo(combo, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        return targetVk != 0 && vkCode == targetVk && CheckModifiersMatch(modifier);
    }

    private bool ConfiguredKeyMatches(string combo, int vkCode)
    {
        if (HotkeyStringFormat.IsDoubleTap(combo, out var tappedKey))
            return KeyboardUtils.GetKeyVirtualCode(tappedKey) == vkCode && CheckModifiersMatch(string.Empty);
        HotkeyStringFormat.ParseCombo(combo, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        return targetVk != 0 && vkCode == targetVk && CheckModifiersMatch(modifier);
    }

    public bool CheckQuickPanelHotkey(int vkCode, out bool consumeKey, uint time = 0)
    {
        MatchedQuickPanelRuleId = FindQuickPanelRule(vkCode, time)?.Id;
        consumeKey = MatchedQuickPanelRuleId != null;
        return consumeKey;
    }

    /// <summary>The global shortcut for opening Quick Navigation in desktop mode.</summary>
    public bool CheckQuickNavigationHotkey(int vkCode, out bool consumeKey, uint time = 0)
    {
        consumeKey = false;
        if (HotkeyStringFormat.IsDoubleTap(_settings.Hotkeys.QuickNavigationHotkey, out _))
        {
            var triggered = MatchConfiguredHotkey(_settings.Hotkeys.QuickNavigationHotkey, vkCode, time);
            consumeKey = triggered;
            return triggered;
        }
        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickNavigationHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        if (targetVk == 0 || vkCode != targetVk || !CheckModifiersMatch(modifier)) return false;

        consumeKey = true;
        return true;
    }

    internal bool CheckFavoriteDoubleTap(int vkCode, uint time, out int ownerIndex)
    {
        ownerIndex = -1;
        foreach (var favorite in _favoriteDoubleTapHotkeys)
        {
            if (_eligibleFavoriteDoubleTapOwners is not null && !_eligibleFavoriteDoubleTapOwners.Contains(favorite.OwnerIndex)) continue;
            if (!MatchConfiguredHotkey(favorite.Hotkey, vkCode, time)) continue;
            ownerIndex = favorite.OwnerIndex;
            return true;
        }
        return false;
    }

    internal void SetEligibleFavoriteDoubleTapOwners(IEnumerable<int> ownerIndices) =>
        _eligibleFavoriteDoubleTapOwners = ownerIndices.ToHashSet();

    private static (int OwnerIndex, string Hotkey)[] BuildEligibleFavoriteDoubleTaps(UserSettings settings)
    {
        var occupiedKeys = new HashSet<int>();
        foreach (var hotkey in HostHotkeys(settings.Hotkeys).Concat(
                     settings.Hotkeys.PluginActionHotkeys.Values.SelectMany(actions => actions.Values)))
            if (TryGetUnmodifiedVirtualKey(hotkey, out var key)) occupiedKeys.Add(key);

        var seen = new HashSet<int>();
        var result = new List<(int OwnerIndex, string Hotkey)>();
        for (var index = 0; index < settings.Favorites.Count; index++)
        {
            var hotkey = settings.Favorites[index].Hotkey;
            if (!HotkeyStringFormat.IsDoubleTap(hotkey, out var keyName)
                || HotkeyStringFormat.IsReservedWindowsShortcut(hotkey)) continue;
            var key = KeyboardUtils.GetKeyVirtualCode(keyName);
            if (key == 0 || occupiedKeys.Contains(key) || !seen.Add(key)) continue;
            result.Add((index, hotkey));
        }
        return result.ToArray();
    }

    private static bool TryGetUnmodifiedVirtualKey(string? hotkey, out int virtualKey)
    {
        virtualKey = 0;
        string modifier, key;
        if (HotkeyStringFormat.IsDoubleTap(hotkey, out var tappedKey))
        {
            modifier = string.Empty;
            key = tappedKey;
        }
        else
        {
            HotkeyStringFormat.ParseCombo(hotkey, out modifier, out key);
            if (!string.IsNullOrEmpty(modifier)) return false;
        }
        virtualKey = KeyboardUtils.GetKeyVirtualCode(key);
        return virtualKey != 0;
    }

    private static IEnumerable<string> HostHotkeys(HotkeyPageSettings hotkeys)
    {
        yield return hotkeys.ToggleWindowHotkey;
        yield return hotkeys.QuickSwitchHotkey;
        yield return hotkeys.QuickPanelHotkey;
        yield return hotkeys.QuickPanelAlternateHotkey;
        yield return hotkeys.QuickNavigationHotkey;
        yield return hotkeys.NextItemHotkey;
        yield return hotkeys.PreviousItemHotkey;
        yield return hotkeys.ActionsMenuHotkey;
        yield return hotkeys.CompleteFromSelectionHotkey;
        yield return hotkeys.QuickLookHotkey;
        yield return hotkeys.KeywordHistoryPreviousHotkey;
        yield return hotkeys.KeywordHistoryNextHotkey;
        yield return hotkeys.KeywordHistoryDeleteHotkey;
        yield return hotkeys.OpenFullWindowHotkey;
        yield return hotkeys.LocalSendSendWindowHotkey;
        yield return hotkeys.StayOpenHotkey;
        foreach (var binding in QuickPanelWakeBindings.Get(hotkeys))
            if (binding.Enabled && binding.Kind == QuickPanelWakeKind.Keyboard)
                yield return binding.Key;
        if (string.IsNullOrWhiteSpace(hotkeys.SelectJumpModifier)) yield break;
        for (var digit = 1; digit <= 9; digit++)
        {
            yield return $"{hotkeys.SelectJumpModifier}+{digit}";
            yield return $"{hotkeys.SelectJumpModifier}+NumPad{digit}";
        }
    }

    public bool CheckAndHandleQuickSwitch(int vkCode, uint time, out bool consumeKey)
    {
        consumeKey = false;
        var triggered = false;
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out var clickModifier))
        {
            if (KeyboardUtils.IsModifierKey(vkCode, clickModifier))
            {
                triggered = _quickSwitchTapDetector.OnModifierKeyDown(vkCode, time);
            }
            else
            {
                _quickSwitchTapDetector.ResetOnOtherKey();
            }
        }
        else
        {
            if (HotkeyStringFormat.IsDoubleTap(_settings.Hotkeys.QuickSwitchHotkey, out _))
            {
                triggered = MatchConfiguredHotkey(_settings.Hotkeys.QuickSwitchHotkey, vkCode, time);
                return TryHandleQuickSwitchNavigation(triggered, out consumeKey);
            }
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickSwitchHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (CheckModifiersMatch(modifier))
                {
                    triggered = true;
                }
            }
        }

        return TryHandleQuickSwitchNavigation(triggered, out consumeKey);
    }

    // Quick Switch's trigger doesn't just toggle a window like the other hotkey does -- it re-navigates
    // the active (dialog) Explorer-like window back to the last folder that was active outside it. Kept as
    // its own method so the gesture-detection above (shared via ModifierDoubleTapDetector) and this
    // navigation policy read as two separate steps, even though they still live in the same class.
    private bool TryHandleQuickSwitchNavigation(bool triggered, out bool consumeKey)
    {
        consumeKey = false;
        if (!triggered) return false;

        // Read without the lock, deliberately. These fields are written under StateLock, but waiting for it
        // here used to drop the keystroke -- the WinEvent thread holds it across cross-process reads, and a
        // low-level hook that overruns LowLevelHooksTimeout is removed by Windows. A stale answer costs one
        // skipped or one futile hand-off, never a wrong window: the tracker thread re-reads the state before
        // it navigates (see QuickSwitchNavigator.Execute).
        if (!_explorerTracker.IsActiveWindowDialog || _explorerTracker.ActiveAdapter == null)
        {
            Logger.Log("[GlobalHotkeyDetector] Quick Switch ignored: no file dialog is tracked.", LogLevel.Info);
            return false;
        }

        // Nothing to switch to, so the keystroke is left to the host window: a dialog that swallows Ctrl+G
        // without navigating is a keystroke the user cannot get back.
        if (string.IsNullOrEmpty(_explorerTracker.LastActiveExplorerPath) && _explorerTracker.LastPathProvider == null)
        {
            Logger.Log("[GlobalHotkeyDetector] Quick Switch ignored: no browsed folder is known yet.", LogLevel.Info);
            return false;
        }

        _requestQuickSwitch(_explorerTracker.ActiveHwnd);
        consumeKey = true;
        return true;
    }
}
