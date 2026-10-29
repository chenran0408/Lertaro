using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.Services.Network;

using Lertaro.Core.Wire;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Query;
namespace Lertaro.Core.Services.Search;

public class SearchService : IDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<List<SearchResult>>> _sessionDirectoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ScopeLiveSearchCache _scopeLiveSearchCache = new();
    private readonly CancellationTokenSource _cacheFillCts = new();
    private readonly SearchPipeClient _pipeClient = new();
    private int _disposed;

    public Task<UsnIndexer.IndexerStatus> GetStatusAsync(CancellationToken token = default) => _pipeClient.GetStatusAsync(token);

    public Task<bool> PingAsync(CancellationToken token = default) => _pipeClient.PingAsync(token);

    // Asks the already-running --service instance to spawn the hook process directly into this caller's
    // own session (see HookProcessBroker) -- the App itself never launches the hook process anymore, so
    // it never has a "runas" UAC prompt of its own to show. requestElevation is only honored server-side
    // when that session's user is genuinely an administrator; otherwise it just launches non-elevated.
    public Task<(bool Ok, int Pid, string? Error)> RequestHookLaunchAsync(bool requestElevation, CancellationToken token = default)
        => _pipeClient.RequestHookLaunchAsync(requestElevation, token);

    // Hands a staged update package to the service for installation. See SearchPipeClient's method of the
    // same name -- Ok means the updater is running, not that the files are in place yet.
    public Task<(bool Ok, string? Error)> RequestApplyUpdateAsync(string sourceDir, CancellationToken token = default)
        => _pipeClient.RequestApplyUpdateAsync(sourceDir, token);

    // Fire-and-forget, called whenever a search window closes/hides (mirrors ShellIconHelper.ClearCache()'s
    // existing trigger points) -- gives back the local drives' per-row full-path memo, which otherwise
    // only self-clears once it crosses its own high backstop threshold (see PathQueryExtensions).
    public Task ClearPathCachesAsync(CancellationToken token = default) => _pipeClient.ClearPathCachesAsync(token);

    // bypassExclusions: opts this one search out of ExcludedPaths/IgnoredPathGlobs/IgnoredPathRegexes
    // filtering. The caller is responsible for stripping whatever query-string marker triggers this
    // (see SearchQuerySortParser.StripExclusionBypass) BEFORE calling here -- `query` itself is always
    // matched/highlighted verbatim, so a caller must never pass the marker through as part of it. A path
    // query does not bypass exclusions by itself. An explicit bypass only affects results returned by the
    // search sources; CheckNeedsLiveSearch decides when the live-scan fallback can provide missing data.
    public async Task<bool> SearchStreamingAsync(string query, int maxResults, int maxAppResults, string? directoryFilter, Action<SearchResult> onResult, CancellationToken token = default, Action? onLocalSearchFailed = null, bool bypassExclusions = false, string? fileNameFilter = null)
    {
        var settings = UserSettings.Load();
        var exclusionRules = ExclusionRuleSet.From(settings);
        // No longer clamped to 2000. The service returns everything that matches and the caller decides
        // what to do with it; asking for a multiple of maxResults existed only to leave headroom for the
        // exclusion filtering below, which is pointless once maxResults is itself unbounded.
        var fileCandidateLimit = maxResults >= int.MaxValue / 4 ? int.MaxValue : Math.Max(maxResults * 4, maxResults);

        var isSearchDir = !string.IsNullOrEmpty(directoryFilter);
        var msg = new SearchRequestMessage
        {
            Id = isSearchDir ? SearchRequestId.SearchDir : SearchRequestId.Search,
            Limit = fileCandidateLimit,
            AppLimit = maxAppResults,
            DirectoryFilter = isSearchDir ? directoryFilter : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Query = query,
            ExactMatch = !settings.EnableFuzzyMatch,
            OrFirstPrecedence = settings.OrFirstPrecedence,
            DisabledAliasComponents = settings.DisabledPluginComponents
                .Where(c => c.Contains("::AliasProvider::", StringComparison.OrdinalIgnoreCase))
                .Concat(settings.DisabledPluginAssemblies.Select(dll => dll + "::AliasProvider::*"))
                .ToList(),
            FileNameFilter = fileNameFilter
        };

        var disabledIds = AliasProviderRegistry.ResolveDisabledProviders(msg.DisabledAliasComponents);
        SearchContext.DisabledAliasIds = disabledIds;
        // Applies to the sources this process searches itself (network drives, live directory scans);
        // the local-drive path carries the same flag over the pipe instead -- see SearchStreamPump.
        SearchContext.FuzzyMatchEnabled = !msg.ExactMatch;
        SearchContext.AndFirstPrecedence = !msg.OrFirstPrecedence;

        var parsed = SearchQueryParser.Parse(query);
        // Path-mode queries still honor global exclusions; only an explicit caller request bypasses them.
        var effectiveBypassExclusions = bypassExclusions;

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uniqueOnResult = new Action<SearchResult>(result =>
        {
            // Unconditional, even in bypass mode: "*" only opts out of the user's own
            // ExcludedPaths/Globs/Regexes configuration, not hidden/system attributes -- those are a
            // separate, always-on filter.
            if (FileSystemItemFilter.IsHiddenOrSystem(result))
                return;

            lock (seenPaths)
            {
                if (!seenPaths.Add(result.Path))
                    return;
            }
            onResult(result);
        });
        var uniqueUnexcludedResult = new Action<SearchResult>(result =>
        {
            if (!ShouldExcludeByUserRules(result, exclusionRules, directoryFilter, effectiveBypassExclusions))
                uniqueOnResult(result);
        });

        var localTask = Task.Run(async () =>
        {
            try
            {
                await SearchPipeClient.SendSearchPipeCommandAsync(msg, result =>
                {
                    uniqueUnexcludedResult(result);
                }, token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Streaming local search failed: {ex.Message}", LogLevel.Error);
                onLocalSearchFailed?.Invoke();
                return false;
            }
        }, token);

        var networkTask = Task.Run(() =>
        {
            try
            {
                return SearchServiceHelper.SearchNetworkDrives(query, fileCandidateLimit, directoryFilter, exclusionRules, effectiveBypassExclusions, uniqueOnResult, token, fileNameFilter);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Network drive search failed: {ex.Message}", LogLevel.Error);
                return false;
            }
        }, token);

        var needsLiveSearch = false;
        var liveScanDir = string.Empty;
        var liveScanFilter = string.Empty;

        if (parsed.IsPathMode && !string.IsNullOrEmpty(parsed.ExactPathLower))
        {
            var resolved = LiveDirectorySearcher.ResolvePathModeSearch(parsed.ExactPathLower);
            if (!string.IsNullOrEmpty(resolved.DirectoryToScan) && SearchServiceHelper.CheckNeedsLiveSearch(resolved.DirectoryToScan, exclusionRules))
            {
                needsLiveSearch = true;
                liveScanDir = resolved.DirectoryToScan;
                liveScanFilter = resolved.FilterQuery;
            }
        }
        else if (!string.IsNullOrEmpty(directoryFilter) && _scopeLiveSearchCache.GetOrAdd(directoryFilter,
            dir => SearchServiceHelper.CheckNeedsLiveSearch(dir, exclusionRules) && Directory.Exists(dir)))
        {
            needsLiveSearch = true;
            liveScanDir = directoryFilter;
            liveScanFilter = query;
        }

        Task<bool>? liveTask = null;
        if (needsLiveSearch && !string.IsNullOrEmpty(liveScanDir))
        {
            var cacheFillToken = _cacheFillCts.Token;
            liveTask = Task.Run(async () =>
            {
                try
                {
                    // GetOrAdd shares ONE in-flight scan per directory across every caller currently
                    // waiting on it, instead of the old lock(this) around the whole SearchService
                    // instance -- that lock serialized every live scan regardless of which directory it
                    // targeted, so typing into a directory that needs one (excluded from the index, an
                    // unconfigured network drive, ...) queued every subsequent keystroke's own scan
                    // attempt behind whichever one happened to go first, none of which could even check
                    // their own cancellation token until they finally got the lock. The shared scan keeps
                    // running when this one query is superseded so the next query in this window can reuse
                    // it, but the SearchService owns its lifetime and cancels it when the window closes.
                    if (_sessionDirectoryCache.Count > 32)
                        _sessionDirectoryCache.Clear();
                    var onlyDirectChildren = parsed.IsPathMode && string.IsNullOrEmpty(liveScanFilter);
                    // liveQuery/onLiveMatch only actually fire for whichever caller wins the GetOrAdd race
                    // (i.e. triggers the scan for real) -- a directory this large/uncached is exactly the
                    // "current folder" case that used to sit with zero results until the entire subtree
                    // walk finished; streaming matches out as each directory is walked keeps the first,
                    // cold keystroke from looking frozen even though the underlying walk cost is unchanged.
                    // KeepObserved because this task outlives whoever started it: a superseded query abandons
                    // the WaitAsync below, and GetOrAdd may discard a factory result outright, so a walk that
                    // faults afterwards -- the dead query's accumulator throwing its cancellation into
                    // onLiveMatch, or the service's own token cancelling at Dispose -- can be left with no
                    // waiter at all. Its finalizer then reports a routine superseded search as a crash.
                    var scanTask = _sessionDirectoryCache.GetOrAdd(liveScanDir,
                        dir => Task.Run(() => LiveDirectorySearcher.ScanDirectory(dir, 10000, cacheFillToken,
                            liveQuery: liveScanFilter, onLiveMatch: uniqueUnexcludedResult,
                            onlyDirectChildren: onlyDirectChildren, parentPath: liveScanDir)).KeepObserved());
                    List<SearchResult> entries;
                    try
                    {
                        entries = await scanTask.WaitAsync(token).ConfigureAwait(false);
                    }
                    catch (Exception) when (scanTask.IsFaulted)
                    {
                        // Do not let a faulted live-scan task poison the cache for every later keystroke.
                        _sessionDirectoryCache.TryRemove(new KeyValuePair<string, Task<List<SearchResult>>>(liveScanDir, scanTask));
                        throw;
                    }

                    return LiveDirectorySearcher.MatchAndStream(entries, liveScanFilter, uniqueUnexcludedResult, token, onlyDirectChildren, liveScanDir);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Log($"[SearchService] Live directory search failed: {ex.Message}", LogLevel.Error);
                    return false;
                }
            }, token);
        }

        var tasks = new List<Task<bool>> { localTask, networkTask };
        if (liveTask != null) tasks.Add(liveTask);

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.Any(r => r);
    }

    // In-memory index lookup only (no disk I/O) -- the most recently modified entries across all of the
    // given directories' subtrees, most recent first. The elevated service only tracks local drive
    // letters, so network/WSL directories are queried in-process here (same split as SearchStreamingAsync's
    // localTask/networkTask) and merged by actual modification time rather than just concatenated.
    public async Task<List<SearchResult>> GetRecentFilesAsync(IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken token = default)
    {
        var networkTask = Task.Run(() =>
        {
            try
            {
                return UserNetworkDriveSearch.GetRecentFiles(directories, limit, maxAgeMinutes);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchService] Network drive GetRecentFiles failed: {ex.Message}", LogLevel.Error);
                return new List<SearchResult>();
            }
        }, token);

        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.GetRecentFiles, Directories = directories.ToList(), Limit = limit, MaxAgeMinutes = maxAgeMinutes }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] GetRecentFiles failed: {resp.Message}", LogLevel.Error);
        var localResults = resp.Kind == PipeResponseKind.RecentFiles && resp.RecentFiles != null ? resp.RecentFiles : new List<SearchResult>();

        var networkResults = await networkTask.ConfigureAwait(false);
        var exclusionRules = ExclusionRuleSet.From(UserSettings.Load());
        return FilterRecentFiles(localResults, networkResults, limit, exclusionRules);
    }

    internal static List<SearchResult> FilterRecentFiles(
        IEnumerable<SearchResult> localResults,
        IEnumerable<SearchResult> networkResults,
        int limit,
        ExclusionRuleSet exclusionRules)
    {
        var merged = localResults.Concat(networkResults)
            .Where(result => !exclusionRules.IsExcludedPath(result.Path, result.IsDir))
            .OrderByDescending(r => r.Metadata.Modified);
        return (limit > 0 ? merged.Take(limit) : merged).ToList();
    }

    internal static bool ShouldExcludeByUserRules(
        SearchResult result,
        ExclusionRuleSet exclusionRules,
        string? directoryFilter,
        bool explicitlyBypassed)
        => !explicitlyBypassed && exclusionRules.IsExcluded(result, directoryFilter);

    // Forwards to the pipe client -- kept on SearchService itself since SearchServiceManagementExtensions
    // and other callers already reach this as an instance method (`service.SendPipeCommandAsync(...)`).
    internal Task<PipeResponse> SendPipeCommandAsync(SearchRequestMessage msg, CancellationToken token)
        => _pipeClient.SendPipeCommandAsync(msg, token);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cacheFillCts.Cancel();
        _cacheFillCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
