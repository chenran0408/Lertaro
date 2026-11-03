namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// Pure helpers for the panel's search highlighting and file-path copy formats.
/// </summary>
internal static class ClipboardPanelText
{
    /// <summary>A contiguous slice of text and whether it is part of the match.</summary>
    public readonly record struct Segment(string Text, bool Highlighted);

    /// <summary>Splits <paramref name="text"/> into runs along the highlight mask. An absent mask
    /// (no query, or no match) yields one unhighlighted segment. Walks surrogate pairs safely:
    /// a mask index set on the low surrogate alone never splits the pair.</summary>
    public static List<Segment> Split(string text, bool[]? mask)
    {
        var segments = new List<Segment>();
        if (text.Length == 0 || mask is not { Length: > 0 })
        {
            if (text.Length > 0) segments.Add(new Segment(text, false));
            return segments;
        }

        var start = 0;
        var startHighlighted = mask[0];
        for (var index = 1; index < text.Length; index++)
        {
            // Never break inside a surrogate pair: a high surrogate takes its low partner along.
            var breakable = !(char.IsHighSurrogate(text[index - 1]) && index < text.Length && char.IsLowSurrogate(text[index]));
            if (breakable && mask[index] != startHighlighted)
            {
                segments.Add(new Segment(text[start..index], startHighlighted));
                start = index;
                startHighlighted = mask[index];
            }
        }
        segments.Add(new Segment(text[start..], startHighlighted));
        return segments;
    }

    /// <summary>Each path double-quoted, one per line -- what Windows' own "copy as path" yields.</summary>
    public static string QuotedPaths(IReadOnlyList<string> paths) =>
        string.Join("\r\n", paths.Select(p => "\"" + p + "\""));

    /// <summary>Windows paths to WSL form: C:\dir\file → /mnt/c/dir/file ( UNC shares become
    /// /mnt/unc-host/share/... which is the common convention).</summary>
    public static string WslPaths(IReadOnlyList<string> paths) =>
        string.Join("\n", paths.Select(ToWsl));

    private static string ToWsl(string path)
    {
        path = path.Trim().TrimEnd('\\', '/');
        // UNC: \\server\share\file → /mnt/server/share/file
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return "/mnt/" + path.TrimStart('\\').Replace('\\', '/');
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
            return $"/mnt/{char.ToLowerInvariant(path[0])}{path[2..].Replace('\\', '/')}";
        return path.Replace('\\', '/');
    }
}
