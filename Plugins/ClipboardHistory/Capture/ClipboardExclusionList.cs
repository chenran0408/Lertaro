namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Pure decision logic for the comma-separated ExcludedProcesses setting so the
// matching rules can be tested without a real clipboard or process list.
internal static class ClipboardExclusionList
{
    public static bool IsExcluded(string? excludedProcesses, string? processName)
    {
        // An unreadable source identity must fail closed: skipping conservatively
        // is the only way to guarantee an excluded app never leaks into history.
        if (string.IsNullOrWhiteSpace(processName)) return true;
        return Parse(excludedProcesses).Contains(Normalize(processName));
    }

    public static HashSet<string> Parse(string? excludedProcesses)
    {
        var entries = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(excludedProcesses)) return entries;
        foreach (var raw in excludedProcesses.Split(','))
        {
            var entry = Normalize(raw);
            if (entry.Length > 0) entries.Add(entry);
        }
        return entries;
    }

    public static string Normalize(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[..^4].Trim();
        return trimmed.ToLowerInvariant();
    }
}
