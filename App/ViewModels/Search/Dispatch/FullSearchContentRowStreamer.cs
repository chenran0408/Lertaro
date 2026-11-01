using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Search.Dispatch;

internal static class FullSearchContentRowStreamer
{
    private const int FullSearchFileRowLimit = 2000;
    private const int FirstContentBatchRows = 60;

    /// <summary>
    /// Walks every content-style file provider on the thread pool and hands its rows over in batches as the
    /// provider finds them -- see
    /// <see cref="SearchQueryDispatchController.OnAdvancedQueryChanged"/>, where it is scheduled onto the search's own debounce tick.
    /// </summary>
    /// <param name="query">The RAW box text, not the stripped query: a content provider recognises its own
    /// trigger word, and the host has already taken that word out of what the file index searches. Handing
    /// it the stripped text would ask it to match a prefix that is no longer there.</param>
    internal static void StreamFullSearchFileRows(string query,
        IEnumerable<IFullSearchFileResultProvider> providers,
        Func<bool> isCurrent,
        Action<List<AppSearchResult>> appendRows)
    {
        var pending = new List<InstantResultItem>();
        IPluginComponent? pendingProvider = null;
        var nextFlush = FirstContentBatchRows;

        void Flush()
        {
            if (pending.Count == 0 || !isCurrent())
                return;

            var rows = new List<AppSearchResult>(pending.Count);
            PluginSearchResultMapper.AddInstantResultItems(rows, pending, query, pendingProvider!);
            pending.Clear();
            if (isCurrent())
                appendRows(rows);
        }

        foreach (var provider in providers)
        {
            // A newer query owns the list, so stop walking instead of mapping rows nobody will paint.
            // Breaking out of the provider's enumeration is what closes its database connection early.
            if (!isCurrent())
                return;

            // Items accumulate under one provider only, because the mapping marks each row with the
            // component it came from -- so a provider switch has to land what is in hand first.
            if (pendingProvider != null && !ReferenceEquals(pendingProvider, provider))
                Flush();
            if (!isCurrent())
                return;

            try
            {
                pendingProvider = provider;
                PluginPerformanceMonitor.Measure(provider, () =>
                {
                    using var items = provider.GetFileResultsStreamed(query, FullSearchFileRowLimit).GetEnumerator();
                    // Check before MoveNext too: the next database read can be expensive. The SDK's
                    // synchronous iterator cannot interrupt a read already in flight, but disposing it
                    // here stops further reads and releases its connection as soon as that read returns.
                    while (isCurrent() && items.MoveNext())
                    {
                        if (!isCurrent())
                            return;
                        pending.Add(items.Current);
                        if (pending.Count < nextFlush)
                            continue;
                        // Growing, because a batch landing in front of the list moves every row already on
                        // screen and that paint has to compare from the top. The first batch is small for the
                        // sake of the user's eyes; the growth keeps a full answer to a handful of
                        // reconciles however many rows the provider has.
                        nextFlush = Math.Min(nextFlush * 4, FullSearchFileRowLimit);
                        Flush();
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchQueryDispatch] Full search file provider '{provider.Name}' failed: {ex.Message}", LogLevel.Error);
            }
        }

        Flush();
    }
}
