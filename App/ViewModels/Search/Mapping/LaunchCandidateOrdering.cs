using System.IO;
using System.Text.RegularExpressions;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Global launch search only. Exact names and match-quality bands beat usage history; application
// entry points break close matches. Folder-scoped and inline searches retain their existing ladder.
internal static class LaunchCandidateOrdering
{
    internal static bool Applies(bool inline, string? scope, bool filtered, bool foldersOnly, string? triggeredType)
        => !inline && string.IsNullOrEmpty(scope) && !filtered && !foldersOnly && triggeredType == null;

    internal static List<SearchResultMapper.RankedCandidate> Order(List<SearchResultMapper.RankedCandidate> candidates,
        string query, Func<string, LaunchShortcut?> readShortcut)
    {
        var explicitExtension = !string.IsNullOrEmpty(Path.GetExtension(query.Trim()));
        var entries = candidates.Select(candidate => (Candidate: candidate,
            Link: Path.GetExtension(candidate.Result.FullPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                ? readShortcut(candidate.Result.FullPath) : null)).ToList();
        var preferredLinks = new HashSet<string>(StringComparer.Ordinal);
        if (!explicitExtension)
            foreach (var entry in entries.Where(entry => entry.Link.HasValue && IsExecutable(entry.Link.Value.Target)))
                preferredLinks.Add(Identity(entry.Link!.Value.Target, entry.Link.Value.Arguments, entry.Link.Value.WorkingDirectory));
        // An equivalent exe may match better than its differently named shortcut. Keep that relevance
        // when replacing it, rather than dropping the useful launch result to the bottom of the list.
        string? LaunchIdentity(AppSearchResult result, LaunchShortcut? link) => result.IsDir ? null
            : link is { } resolved && IsExecutable(resolved.Target) ? Identity(resolved.Target, resolved.Arguments, resolved.WorkingDirectory)
            : IsExecutable(result.FullPath) ? Identity(result.FullPath, "", "") : null;
        var scores = entries.Where(entry => LaunchIdentity(entry.Candidate.Result, entry.Link) != null)
            .GroupBy(entry => LaunchIdentity(entry.Candidate.Result, entry.Link)!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Candidate)
                .OrderByDescending(candidate => ExactName(candidate.Result, query))
                .ThenBy(candidate => candidate.Match.Start).ThenByDescending(candidate => candidate.Match.Weight)
                .ThenBy(candidate => candidate.Match.Tier).First(), StringComparer.Ordinal);
        var rankedEntries = entries.Select(entry => (entry.Candidate, entry.Link,
            Score: !explicitExtension && LaunchIdentity(entry.Candidate.Result, entry.Link) is { } identity
                ? scores[identity] : entry.Candidate));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var launches = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<SearchResultMapper.RankedCandidate>();
        foreach (var entry in rankedEntries
            .OrderByDescending(entry => ExactName(entry.Score.Result, query))
            // Equal exact names are a launch request: Steam.exe/Steam.lnk must beat a Steam folder,
            // even though the extension lowers coverage and the default type order prefers files.
            .ThenBy(entry => ExactName(entry.Score.Result, query) ? LaunchRank(entry.Candidate.Result, entry.Link) : 0)
            .ThenBy(entry => entry.Score.Match.Start)
            // Coverage weights are 0..1. A tenth-wide band allows usage/launch preferences to settle
            // near matches without letting an unrelated old result outrank a much stronger match.
            .ThenByDescending(entry => Math.Floor(entry.Score.Match.Weight * 10))
            .ThenBy(entry => LaunchRank(entry.Candidate.Result, entry.Link))
            .ThenBy(entry => entry.Candidate.TypeRank)
            .ThenByDescending(entry => entry.Candidate.IsCurated)
            .ThenBy(entry => entry.Candidate.Priority)
            .ThenByDescending(entry => entry.Score.Match.Weight)
            .ThenBy(entry => entry.Score.Match.Tier)
            .ThenBy(entry => entry.Link?.Arguments.Length > 0 ? 1 : 0)
            .ThenBy(entry => entry.Candidate.NormalizedPath.Length)
            .ThenBy(entry => entry.Candidate.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            var result = entry.Candidate.Result;
            if (!paths.Add(entry.Candidate.NormalizedPath)) continue;
            if (!explicitExtension && !result.IsDir)
            {
                var identity = entry.Link is { } link && IsExecutable(link.Target)
                    ? Identity(link.Target, link.Arguments, link.WorkingDirectory)
                    : IsExecutable(result.FullPath) ? Identity(result.FullPath, "", "") : null;
                if (identity != null)
                {
                    if (entry.Link == null && preferredLinks.Contains(identity)) continue;
                    if (!launches.Add(identity)) continue;
                }
            }
            results.Add(entry.Candidate);
        }
        return results;
    }

    internal static bool ExactName(AppSearchResult result, string query)
    {
        query = query.Trim();
        return query.Equals(result.Name, StringComparison.OrdinalIgnoreCase)
            || (!result.IsDir && string.IsNullOrEmpty(Path.GetExtension(query))
                && query.Equals(Path.GetFileNameWithoutExtension(result.FullPath), StringComparison.OrdinalIgnoreCase));
    }

    internal static int LaunchRank(AppSearchResult result, LaunchShortcut? link)
    {
        if (result.IsDir) return 2;
        var target = link?.Target ?? result.FullPath;
        if (IsExecutable(target)) return IsSupportExecutable(target) ? 3 : link.HasValue || result.IsApplication ? 0 : 1;
        // A successfully read folder/document link stays an ordinary file even if a provider labeled it Application.
        return link.HasValue || Path.GetExtension(result.FullPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? 2 : result.IsApplication ? 0 : 2;
    }

    private static bool IsExecutable(string path) => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    internal static bool IsSupportExecutable(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        // Conservative filename heuristic only; an exact name still wins when the user wants this tool.
        return Regex.IsMatch(name, @"^(unins\d*|uninstall(er)?|setup|installer|update(r)?|crashreporter|helper)$|(^|[-_. ])(uninstall(er)?|setup|installer|update(r)?|crashreporter|helper)([-_. ]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || name.EndsWith("Updater", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Uninstaller", StringComparison.OrdinalIgnoreCase)
            || name.Contains("卸载", StringComparison.Ordinal) || name.Contains("更新", StringComparison.Ordinal);
    }

    private static string Identity(string target, string arguments, string directory)
    {
        target = target.Replace('/', '\\').Trim();
        directory = string.IsNullOrWhiteSpace(directory) ? Path.GetDirectoryName(target) ?? "" : directory.Replace('/', '\\').Trim().TrimEnd('\\');
        // Argument case is significant; two shortcuts with different arguments/directories remain separate choices.
        return target.ToUpperInvariant() + "\0" + arguments + "\0" + directory.ToUpperInvariant();
    }
}
