using System.IO;
using Lertaro.Core.SearchIndex;
using System.Text.RegularExpressions;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Global launch search only. Exact names and match-quality bands beat usage history; application
// entry points break close matches. Folder-scoped and inline searches retain their existing ladder.
internal static class LaunchCandidateOrdering
{
    internal static bool Applies(bool inline, string? scope, bool filtered, bool foldersOnly, string? triggeredType)
        => !inline && string.IsNullOrEmpty(scope) && !filtered && !foldersOnly && triggeredType == null;

    internal static List<SearchResultMapper.RankedCandidate> Order(List<SearchResultMapper.RankedCandidate> candidates,
        string query, Func<string, LaunchShortcut?> readShortcut, IReadOnlyDictionary<string, int>? usageCounts = null)
    {
        var queryName = query.Trim();
        var explicitExtension = !string.IsNullOrEmpty(Path.GetExtension(queryName))
            && (!candidates.Any(candidate => candidate.IsExactAliasMatch)
                || candidates.Any(candidate => !candidate.Result.IsApplication && !candidate.Result.IsDir
                    && queryName.Equals(Path.GetFileName(candidate.Result.FullPath), StringComparison.OrdinalIgnoreCase)));
        var entries = candidates.Select(candidate =>
        {
            var result = candidate.Result;
            var link = !explicitExtension && !result.IsDir && Path.GetExtension(result.FullPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                ? readShortcut(result.FullPath) : null;
            var identity = explicitExtension ? null : LaunchIdentity(result, link);
            var opens = usageCounts?.GetValueOrDefault(candidate.NormalizedPath) ?? 0;
            return (Candidate: candidate, Link: link, Identity: identity, Opens: opens);
        }).ToList();
        // Transfer relevance and usage to the representative shortcut. The same path may arrive
        // from the catalog, the index and history, so count it only once.
        var launchesByIdentity = entries.Where(entry => entry.Identity != null)
            .GroupBy(entry => entry.Identity!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (
                Score: group.Select(entry => entry.Candidate)
                    .OrderByDescending(candidate => ExactMatch(candidate, query, explicitExtension))
                    .ThenByDescending(candidate => ExactName(candidate.Result, queryName))
                    .ThenBy(candidate => candidate.Match.Shape).ThenBy(candidate => candidate.Match.Start)
                    .ThenByDescending(candidate => candidate.Match.Weight).ThenBy(candidate => candidate.Match.Tier).First(),
                Opens: group.GroupBy(entry => entry.Candidate.NormalizedPath, StringComparer.OrdinalIgnoreCase).Sum(path => (long)path.Max(entry => entry.Opens)),
                Curated: group.Any(entry => entry.Candidate.IsCurated),
                Priority: group.Min(entry => entry.Candidate.Priority),
                HasShortcut: group.Any(entry => entry.Link != null)), StringComparer.Ordinal);
        // Only compare visible labels for competing catalog shortcuts to the same launch command.
        // The executable alias is shared by those rows and cannot choose a recognizable label.
        FuzzyQuery? titleQuery = null;
        var weakerLabels = new HashSet<AppSearchResult>();
        foreach (var group in entries.Where(e => e.Identity != null && e.Link.HasValue && e.Candidate.Result.IsApplication)
            .GroupBy(e => e.Identity!, StringComparer.Ordinal))
        {
            var labels = group.Select(e => e.Candidate.Result).ToList();
            if (labels.All(r => string.Equals(r.Name, labels[0].Name, StringComparison.OrdinalIgnoreCase))) continue;
            titleQuery ??= FuzzyQuery.Parse(queryName);
            var ranked = labels.Select(row => (Row: row, Match: titleQuery.Value.BestMatch(row.Name)))
                .OrderBy(e => e.Match.Shape).ThenBy(e => e.Match.Start).ThenByDescending(e => e.Match.Weight)
                .ThenBy(e => e.Match.Tier).ToList();
            foreach (var entry in ranked.Where(e => e.Match != ranked[0].Match))
                weakerLabels.Add(entry.Row);
        }
        var rankedEntries = entries.Where(entry => !weakerLabels.Contains(entry.Candidate.Result)).Select(entry =>
        {
            var signal = entry.Identity is { } key ? launchesByIdentity[key]
                : (entry.Candidate, (long)entry.Opens, entry.Candidate.IsCurated, entry.Candidate.Priority, false);
            return (entry.Candidate, entry.Link, entry.Identity, Score: signal.Item1, Opens: signal.Item2,
                Curated: signal.Item3, Priority: signal.Item4, HasShortcut: signal.Item5,
                KindRank: LaunchRank(entry.Candidate.Result, entry.Link),
                Exact: ExactMatch(signal.Item1, query, explicitExtension));
        });
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var launches = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<SearchResultMapper.RankedCandidate>();
        foreach (var entry in rankedEntries
            .OrderByDescending(entry => entry.Exact)
            // Equal exact names are a launch request: Steam.exe/Steam.lnk must beat a Steam folder,
            // even though the extension lowers coverage and the default type order prefers files.
            .ThenBy(entry => entry.Exact && LaunchRank(entry.Candidate.Result, entry.Link, exact: true) >= 2)
            // A match against the complete name is equally strong with or without ".exe" or ".lnk".
            .ThenBy(entry => entry.Exact ? default : entry.Score.Match.Shape)
            .ThenBy(entry => entry.Exact ? 0 : entry.Score.Match.Start)
            // Coverage weights are 0..1. A tenth-wide band allows usage/launch preferences to settle
            // near matches without letting an unrelated old result outrank a much stronger match.
            .ThenByDescending(entry => entry.Exact ? 0 : Math.Floor(entry.Score.Match.Weight * 10))
            .ThenBy(entry => !entry.Exact && entry.KindRank == 3)
            .ThenByDescending(entry => entry.Curated)
            .ThenByDescending(entry => entry.Opens)
            .ThenBy(entry => entry.Priority)
            .ThenBy(entry => entry.KindRank)
            // Preserve the catalog's app title/icon/callback when the index returns that same shortcut.
            // The files-first setting otherwise replaces "QQ" with the raw "QQ.lnk" file row.
            .ThenByDescending(entry => entry.Candidate.Result.IsApplication && entry.KindRank == 0)
            .ThenByDescending(entry => entry.Exact && ExactName(entry.Score.Result, queryName))
            .ThenBy(entry => entry.Candidate.TypeRank)
            .ThenBy(entry => entry.Candidate.RecencyBand)
            .ThenBy(entry => entry.Candidate.Depth)
            .ThenByDescending(entry => entry.Score.Match.Weight)
            .ThenBy(entry => entry.Score.Match.Tier)
            .ThenBy(entry => entry.Link?.Arguments.Length > 0 ? 1 : 0)
            .ThenBy(entry => entry.Candidate.NormalizedPath.Length)
            .ThenBy(entry => entry.Candidate.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            var result = entry.Candidate.Result;
            var pathKey = entry.Candidate.NormalizedPath.ToUpperInvariant();
            if (result.IsApplication && (!string.IsNullOrEmpty(result.ApplicationArguments) || !string.IsNullOrEmpty(result.ApplicationWorkingDirectory)))
                pathKey += "\0" + result.ApplicationArguments + "\0" + result.ApplicationWorkingDirectory.ToUpperInvariant();
            if (!paths.Add(pathKey)) continue;
            if (entry.Identity is { } identity && ((entry.Link == null && entry.HasShortcut) || !launches.Add(identity)))
                continue;
            results.Add(entry.Candidate);
        }
        // A single program fits on one line. When two launch profiles share a title, expose
        // enough of the launch command to choose the intended installation or working folder.
        foreach (var group in results.Where(c => c.Result.IsApplication)
            .GroupBy(c => c.Result.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Skip(1).Any()))
        {
            foreach (var candidate in group)
            {
                var app = candidate.Result;
                app.ParentDir = app.FullPath;
                if (app.ApplicationArguments.Length > 0)
                    app.ParentDir += " " + app.ApplicationArguments;
                if (app.ApplicationWorkingDirectory.Length > 0)
                    app.ParentDir += "  ·  " + app.ApplicationWorkingDirectory;
            }
        }
        return results;
    }

    // A full pinyin/initials alias is as exact as the display name. Requiring full coverage avoids
    // promoting a weak fuzzy hit; an explicit extension still means the user requested that file.
    private static bool ExactMatch(SearchResultMapper.RankedCandidate candidate, string query, bool explicitExtension)
        => explicitExtension
            ? query.Trim().Equals(Path.GetFileName(candidate.Result.FullPath), StringComparison.OrdinalIgnoreCase)
            : ExactName(candidate.Result, query) || candidate.IsExactAliasMatch
                || (candidate.Match.IsMatch && candidate.Match.Start == 0 && candidate.Match.Weight >= 1);

    private static string? LaunchIdentity(AppSearchResult result, LaunchShortcut? link)
        => result.IsDir ? null
            : link is { } resolved && IsExecutable(resolved.Target) ? Identity(resolved.Target, resolved.Arguments, LinkDirectory(result, resolved))
            : IsExecutable(result.FullPath) ? Identity(result.FullPath, result.ApplicationArguments, result.ApplicationWorkingDirectory) : null;

    internal static bool ExactName(AppSearchResult result, string query)
    {
        query = query.Trim();
        return query.Equals(result.Name, StringComparison.OrdinalIgnoreCase)
            || (!result.IsDir && string.IsNullOrEmpty(Path.GetExtension(query))
                && query.Equals(Path.GetFileNameWithoutExtension(result.FullPath), StringComparison.OrdinalIgnoreCase));
    }

    internal static int LaunchRank(AppSearchResult result, LaunchShortcut? link, bool exact = false)
    {
        if (result.IsDir) return 2;
        var target = link?.Target ?? result.FullPath;
        if (IsExecutable(target)) return !exact && IsSupportExecutable(target) ? 3 : link.HasValue || result.IsApplication ? 0 : 1;
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

    private static string LinkDirectory(AppSearchResult result, LaunchShortcut link)
        => string.IsNullOrWhiteSpace(result.ApplicationWorkingDirectory) ? link.WorkingDirectory : result.ApplicationWorkingDirectory;

    private static string Identity(string target, string arguments, string directory)
    {
        target = target.Replace('/', '\\').Trim();
        directory = string.IsNullOrWhiteSpace(directory) ? Path.GetDirectoryName(target) ?? "" : directory.Replace('/', '\\').Trim().TrimEnd('\\');
        // Argument case is significant; two shortcuts with different arguments/directories remain separate choices.
        return target.ToUpperInvariant() + "\0" + arguments + "\0" + directory.ToUpperInvariant();
    }
}
