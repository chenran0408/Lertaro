namespace Lertaro.App.ViewModels.QuickPanel;

// Positions are independent of enumeration order; missing files retain their reserved cells.
internal static class QuickPanelGridLayout
{
    internal const int MaximumSlot = 100000;

    internal static Dictionary<string, int> Merge(IReadOnlyDictionary<string, int> saved, IEnumerable<string> paths)
    {
        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<int>();
        foreach (var pair in saved)
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value >= 0 && pair.Value <= MaximumSlot && used.Add(pair.Value))
                slots.TryAdd(pair.Key, pair.Value);
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (slots.ContainsKey(path)) continue;
            var next = 0;
            while (used.Contains(next)) next++;
            slots[path] = next;
            used.Add(next);
        }
        return slots;
    }

    internal static Dictionary<string, int> Place(IReadOnlyDictionary<string, int> saved, IEnumerable<string> paths, int target)
    {
        var slots = Merge(saved, Array.Empty<string>());
        if (target < 0 || target > MaximumSlot) return slots;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hadSlot = slots.TryGetValue(path, out var original);
            if (hadSlot && original == target) { target++; continue; }
            var occupant = slots.FirstOrDefault(pair => pair.Value == target && !pair.Key.Equals(path, StringComparison.OrdinalIgnoreCase)).Key;
            slots.Remove(path);
            if (occupant != null)
            {
                var free = hadSlot ? original : 0;
                while (!hadSlot && slots.Values.Contains(free)) free++;
                slots[occupant] = free;
            }
            slots[path] = target;
            // Multiple dropped items fill consecutive free cells without disturbing other icons.
            do { target++; } while (slots.Values.Contains(target));
        }
        return slots;
    }
}
