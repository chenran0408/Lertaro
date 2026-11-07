namespace Lertaro.Core.Extensions;

/// <summary>
/// A1: pure path formatting for the "Copy path" action variants. Static, no IO, no state.
/// </summary>
public static class PathFormatHelper
{
    /// <summary>Paths wrapped in double quotes (shell-safe for paths containing spaces).</summary>
    public static IEnumerable<string> ToQuoted(IEnumerable<string> paths) =>
        paths.Select(p => $"\"{p}\"");

    /// <summary>Backslashes replaced with forward slashes (many web tools and Unix-adjacent tools prefer these).</summary>
    public static IEnumerable<string> ToForwardSlash(IEnumerable<string> paths) =>
        paths.Select(p => p.Replace('\\', '/'));

    /// <summary>Local paths rewritten as UNC (\\localhost\c$\...); already-UNC and non-drive paths pass through.</summary>
    public static IEnumerable<string> ToUnc(IEnumerable<string> paths) =>
        paths.Select(p =>
        {
            if (p.StartsWith(@"\\") || p.Length < 2 || p[1] != ':') return p;
            return $"\\\\localhost\\{char.ToLowerInvariant(p[0])}${p[2..]}";
        });

    /// <summary>Just the file name (with extension), no directory.</summary>
    public static IEnumerable<string> ToFileName(IEnumerable<string> paths) =>
        paths.Select(Path.GetFileName);

    /// <summary>The containing directory (no file name, no trailing separator).</summary>
    public static IEnumerable<string> ToDirectory(IEnumerable<string> paths) =>
        paths.Select(p => Path.GetDirectoryName(Path.GetFullPath(p)) ?? p);
}
