using System.IO;
using System.Text.RegularExpressions;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Build application name variants once during catalog loading, rather than during each result paint.
internal static class ApplicationSearchAliases
{
    internal static List<string> Collect(string title, string launchPath, Func<string, LaunchShortcut?> readShortcut)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = Regex.Matches(Regex.Replace(title, @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " "), @"[A-Za-z0-9]+").Select(match => match.Value).ToArray();
        if (words.Length > 1)
        {
            aliases.Add(string.Concat(words));
            for (var start = 0; start < words.Length - 1; start++)
            {
                var initials = string.Concat(words.Skip(start).Select(word => word[0]));
                aliases.Add(initials);
                aliases.Add(string.Concat(words.Skip(start).Take(words.Length - start - 1).Select(word => word[0])) + words[^1]);
            }
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

    internal static bool IsExactMatch(string query, IEnumerable<string> aliases, IEnumerable<char> separators)
    {
        var ignored = separators.Where(separator => separator != '\0').ToHashSet();
        string Normalize(string text) => string.Concat(text.Where(character => !char.IsWhiteSpace(character) && !ignored.Contains(character)));
        var normalized = Normalize(query);
        return normalized.Length > 0 && aliases.Any(group => group.Split('|').Any(alias =>
            normalized.Equals(Normalize(alias), StringComparison.OrdinalIgnoreCase)));
    }
}
