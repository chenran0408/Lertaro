using Flow.Launcher.Plugin;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Routes queries to appropriate Flow plugins based on action keywords or global wildcard registration.
/// </summary>
public class FlowQueryDispatcher
{
    private readonly FlowPluginHost _host;
    private readonly TimeSpan _queryTimeout;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _queryGates = new(StringComparer.OrdinalIgnoreCase);

    public FlowQueryDispatcher(FlowPluginHost host) : this(host, TimeSpan.FromSeconds(2)) { }

    internal FlowQueryDispatcher(FlowPluginHost host, TimeSpan queryTimeout)
    {
        _host = host;
        _queryTimeout = queryTimeout;
    }

    public Query ParseQuery(string rawInput)
    {
        var trimmed = rawInput?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            return new Query
            {
                OriginalQuery = rawInput ?? string.Empty,
                TrimmedQuery = string.Empty,
                Search = string.Empty,
                SearchTerms = [],
                ActionKeyword = string.Empty
            };
        }

        var firstSpace = trimmed.IndexOf(' ');
        var firstWord = firstSpace > 0 ? trimmed[..firstSpace] : trimmed;

        if (_host.KeywordPlugins.TryGetValue(firstWord, out _))
        {
            var search = firstSpace > 0 ? trimmed[(firstSpace + 1)..].TrimStart() : string.Empty;
            return new Query
            {
                OriginalQuery = rawInput ?? string.Empty,
                TrimmedQuery = trimmed,
                ActionKeyword = firstWord,
                Search = search,
                SearchTerms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            };
        }

        return new Query
        {
            OriginalQuery = rawInput ?? string.Empty,
            TrimmedQuery = trimmed,
            ActionKeyword = string.Empty,
            Search = trimmed,
            SearchTerms = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        };
    }

    public async Task<List<Result>> DispatchQueryAsync(string rawInput, CancellationToken token = default)
    {
        var query = ParseQuery(rawInput);
        var targetPlugins = GetTargetPlugins(query.ActionKeyword);

        if (targetPlugins.Count == 0)
            return [];

        _host.NotifyVisibilityChanged(targetPlugins, true);

        var tasks = targetPlugins.Select(async pair =>
        {
            if (token.IsCancellationRequested) return [];
            var started = Stopwatch.GetTimestamp();
            var gate = _queryGates.GetOrAdd(pair.Metadata.ID, _ => new SemaphoreSlim(1, 1));
            try { if (!await gate.WaitAsync(_queryTimeout, token).ConfigureAwait(false)) return []; }
            catch (OperationCanceledException) { return []; }
            var remaining = _queryTimeout - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) { gate.Release(); return []; }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            // Run outside the caller: a plugin may block before it even returns a Task.
            var queryTask = Task.Run(async () => await pair.Plugin.QueryAsync(query, cancellation.Token).ConfigureAwait(false));
            _ = queryTask.ContinueWith(completed =>
            {
                _ = completed.Exception;
                gate.Release();
                cancellation.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            try
            {
                var results = await queryTask.WaitAsync(remaining, token).ConfigureAwait(false);
                if (results != null)
                {
                    foreach (var res in results)
                    {
                        if (string.IsNullOrEmpty(res.PluginDirectory))
                            res.PluginDirectory = pair.Metadata.PluginDirectory;
                        res.OriginQuery = query;
                    }
                    return results;
                }
            }
            catch (OperationCanceledException)
            {
                try { cancellation.Cancel(); } catch { /* Plugin cancellation callbacks may themselves throw. */ }
                // Normal query cancellation
            }
            catch (TimeoutException)
            {
                try { cancellation.Cancel(); } catch { /* Plugin cancellation callbacks may themselves throw. */ }
                PluginSdk.Logger.Log($"插件 {pair.Metadata.Name} 响应较慢，已跳过本次搜索。", PluginSdk.LogLevel.Warn);
            }
            catch
            {
                // Suppress plugin query exceptions to protect launcher stability
            }
            return (List<Result>)[];
        });

        var resultsArray = await Task.WhenAll(tasks);
        return resultsArray.SelectMany(r => r).OrderByDescending(r => r.Score).ToList();
    }

    private IReadOnlyList<PluginPair> GetTargetPlugins(string actionKeyword)
    {
        if (!string.IsNullOrEmpty(actionKeyword) && _host.KeywordPlugins.TryGetValue(actionKeyword, out var specificPlugins))
        {
            return specificPlugins.Where(pair => !pair.Metadata.Disabled && !_host.IsLoading(pair.Metadata.ID)).ToList();
        }

        return _host.GlobalPlugins.Where(pair => !pair.Metadata.Disabled && !_host.IsLoading(pair.Metadata.ID)).ToList();
    }
}
