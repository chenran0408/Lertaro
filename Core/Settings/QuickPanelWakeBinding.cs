namespace Lertaro.Core;

public enum QuickPanelWakeKind { Keyboard, Hold, Click, Circle }
public enum QuickPanelWakeButton { Right, Middle, Back, Forward }
public enum QuickPanelCircleDirection { Either, Clockwise, Counterclockwise }

public sealed class QuickPanelWakeBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    public QuickPanelWakeKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public QuickPanelWakeButton Button { get; set; } = QuickPanelWakeButton.Right;
    public int HoldMilliseconds { get; set; } = 600;
    public int MovementTolerance { get; set; } = 6;
    public int CircleTurns { get; set; } = 2;
    public QuickPanelCircleDirection CircleDirection { get; set; }
    public int MinimumRadius { get; set; } = 24;
    public int MaximumRadius { get; set; } = 250;

    public QuickPanelWakeBinding Copy() => (QuickPanelWakeBinding)MemberwiseClone();
}

public static class QuickPanelWakeBindings
{
    // Null means an older settings file. An explicitly empty list means the user removed all bindings.
    public static IReadOnlyList<QuickPanelWakeBinding> Get(HotkeyPageSettings settings)
    {
        if (settings.QuickPanelWakeBindings is { } bindings) return bindings.Where(b => b != null).ToArray();
        var migrated = new List<QuickPanelWakeBinding>();
        if (!string.IsNullOrWhiteSpace(settings.QuickPanelHotkey))
            migrated.Add(new() { Id = "legacy-key", Key = settings.QuickPanelHotkey });
        if (!string.IsNullOrWhiteSpace(settings.QuickPanelAlternateHotkey))
            migrated.Add(new() { Id = "legacy-alternate", Key = settings.QuickPanelAlternateHotkey });
        migrated.Add(new() { Id = "legacy-hold", Kind = QuickPanelWakeKind.Hold,
            Enabled = settings.QuickPanelMouseRightHold, HoldMilliseconds = Math.Clamp(settings.QuickPanelRightHoldMilliseconds, 300, 2000) });
        migrated.Add(new() { Id = "legacy-circle", Kind = QuickPanelWakeKind.Circle, Enabled = settings.QuickPanelMouseCircle });
        return migrated;
    }

    public static string? Validate(QuickPanelWakeBinding binding)
    {
        if (!Enum.IsDefined(binding.Kind) || !Enum.IsDefined(binding.Button) || !Enum.IsDefined(binding.CircleDirection)
            || string.IsNullOrWhiteSpace(binding.Id) || binding.Id.Length > 100) return "Wake_InvalidRule";
        if (binding.Kind == QuickPanelWakeKind.Keyboard)
        {
            var isDoubleTap = HotkeyStringFormat.IsDoubleTap(binding.Key, out var tappedKey);
            HotkeyStringFormat.ParseCombo(binding.Key ?? "", out var modifier, out var key);
            if ((!isDoubleTap && string.IsNullOrWhiteSpace(modifier)) || modifier.Equals("None", StringComparison.OrdinalIgnoreCase)
                || HotkeyStringFormat.IsBareModifier(binding.Key ?? "", out _)
                || (!isDoubleTap && modifier.Split('+').Any(part => part.ToUpperInvariant() is not ("CONTROL" or "CTRL" or "ALT" or "SHIFT" or "WIN")))
                || HotkeyStringFormat.IsReservedWindowsShortcut(binding.Key)
                || Hook.InlineSearch.KeyboardUtils.GetKeyVirtualCode(isDoubleTap ? tappedKey : key) is 0 or 0x7B) return "Wake_InvalidKey";
        }
        if (binding.Kind == QuickPanelWakeKind.Click && binding.Button == QuickPanelWakeButton.Right) return "Wake_InvalidClick";
        if (binding.HoldMilliseconds is < 300 or > 2000 || binding.MovementTolerance is < 3 or > 30
            || binding.CircleTurns is < 1 or > 3 || binding.MinimumRadius is < 12 or > 150
            || binding.MaximumRadius is < 30 or > 500 || binding.MinimumRadius >= binding.MaximumRadius) return "Wake_InvalidRange";
        return null;
    }

    public static bool Conflicts(QuickPanelWakeBinding first, QuickPanelWakeBinding second)
    {
        if (!first.Enabled || !second.Enabled) return false;
        if (first.Kind == QuickPanelWakeKind.Keyboard || second.Kind == QuickPanelWakeKind.Keyboard)
            return first.Kind == second.Kind && SameKey(first.Key, second.Key);
        if (first.Kind == QuickPanelWakeKind.Circle || second.Kind == QuickPanelWakeKind.Circle)
            return first.Kind == second.Kind && (first.CircleDirection == QuickPanelCircleDirection.Either
                || second.CircleDirection == QuickPanelCircleDirection.Either || first.CircleDirection == second.CircleDirection);
        return first.Button == second.Button;
    }

    public static bool SameKey(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        var firstDoubleTap = HotkeyStringFormat.IsDoubleTap(first, out var firstTapKey);
        var secondDoubleTap = HotkeyStringFormat.IsDoubleTap(second, out var secondTapKey);
        if (firstDoubleTap || secondDoubleTap)
        {
            var tapKey = firstDoubleTap ? firstTapKey : secondTapKey;
            var other = firstDoubleTap ? second : first;
            var tapVirtualKey = Hook.InlineSearch.KeyboardUtils.GetKeyVirtualCode(tapKey);
            if (firstDoubleTap && secondDoubleTap)
                return tapVirtualKey == Hook.InlineSearch.KeyboardUtils.GetKeyVirtualCode(secondTapKey);

            HotkeyStringFormat.ParseCombo(other.Replace(" ", ""), out var otherModifiers, out var otherKey);
            return string.IsNullOrWhiteSpace(otherModifiers)
                && tapVirtualKey == Hook.InlineSearch.KeyboardUtils.GetKeyVirtualCode(otherKey);
        }
        HotkeyStringFormat.ParseCombo(first.Replace(" ", ""), out var firstModifiers, out var firstKey);
        HotkeyStringFormat.ParseCombo(second.Replace(" ", ""), out var secondModifiers, out var secondKey);
        return Hook.InlineSearch.KeyboardUtils.GetKeyVirtualCode(firstKey) == Hook.InlineSearch.KeyboardUtils.GetKeyVirtualCode(secondKey)
            && NormalizeModifiers(firstModifiers).SetEquals(NormalizeModifiers(secondModifiers));
    }

    private static HashSet<string> NormalizeModifiers(string modifiers) => modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Trim().ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => "CTRL",
            "ALT" => "ALT",
            "SHIFT" => "SHIFT",
            "WIN" or "WINDOWS" => "WIN",
            _ => part.Trim().ToUpperInvariant()
        }).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
