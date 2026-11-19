using System.IO;
using System.Text.RegularExpressions;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Build application name variants once during catalog loading, rather than during each result paint.
internal static class ApplicationSearchAliases
{
    internal static List<string> Collect(string title, string launchPath, Func<string, LaunchShortcut?> readShortcut)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddTitleAliases(title, aliases);
        // Start-menu labels often append a location or architecture, e.g. "Visual Studio Code (D drive)".
        // Keep the full name searchable and derive the same shortcuts as the unqualified product name.
        var product = Regex.Replace(title, @"(?:\s*[（(][^()（）]*[)）]|\s*\[[^\[\]]*\])+\s*$", "").TrimEnd();
        if (product.Length > 0 && product != title)
        {
            aliases.Add(product);
            AddTitleAliases(product, aliases);
        }
        var target = Path.GetExtension(launchPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? readShortcut(launchPath)?.Target : launchPath;
        if (!string.IsNullOrWhiteSpace(target) && Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(target);
            // A shared host is not the application's identity (many unrelated apps use cmd/java/etc.).
            if (!Regex.IsMatch(name, @"^(cmd|powershell|pwsh|explorer|rundll32|wscript|cscript|mshta|mmc|control|javaw?|pythonw?|node|dotnet)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                aliases.Add(name);
        }
        aliases.RemoveWhere(alias => alias.Length < 2 || alias.Equals(title, StringComparison.OrdinalIgnoreCase));
        return aliases.ToList();
    }

    private static void AddTitleAliases(string title, HashSet<string> aliases)
    {
        var words = Regex.Matches(Regex.Replace(title, @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " "), @"[A-Za-z0-9]+").Select(match => match.Value).ToArray();
        foreach (Match word in Regex.Matches(title, @"[A-Za-z][A-Za-z0-9]+")) aliases.Add(word.Value);
        if (words.Length > 1)
        {
            aliases.Add(string.Concat(words));
            // Allow an acronym to omit a trailing version or edition, as in CFS for Copper Finch Studio 2026.
            var titleInitials = string.Concat(words.Select(word => word[0]));
            for (var length = 3; length < titleInitials.Length; length++)
                aliases.Add(titleInitials[..length]);
            for (var start = 0; start < words.Length - 1; start++)
            {
                var initials = string.Concat(words.Skip(start).Select(word => word[0]));
                aliases.Add(initials);
                aliases.Add(string.Concat(words.Skip(start).Take(words.Length - start - 1).Select(word => word[0])) + words[^1]);
            }
        }
    }

    internal static HashSet<string> BuildExactAliases(IEnumerable<string> aliases, char[] separators) =>
        aliases.SelectMany(group => group.Split('|')).Select(alias => Normalize(alias, separators))
            .Where(alias => alias.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static string Normalize(string text, char[] separators) =>
        text.Any(c => char.IsWhiteSpace(c) || Array.IndexOf(separators, c) >= 0)
            ? string.Concat(text.Where(c => !char.IsWhiteSpace(c) && Array.IndexOf(separators, c) < 0))
            : text;
}
