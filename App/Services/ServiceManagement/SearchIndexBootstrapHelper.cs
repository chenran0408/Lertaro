using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.Services;

internal static class SearchIndexBootstrapHelper
{
    internal const string IndexError = "index-error";

    public static async Task<UsnIndexer.IndexerStatus> EnsureInitializedAsync(SearchService searchService, bool forceRebuild = false)
    {
        if (forceRebuild)
        {
            await searchService.InitializeOrLoadIndexAsync(true).ConfigureAwait(false);
            return new UsnIndexer.IndexerStatus { State = "force-rebuild" };
        }

        var status = await WaitForStartupStatusAsync(searchService).ConfigureAwait(false);
        if (status.State is "idle" or IndexError)
        {
            await searchService.InitializeOrLoadIndexAsync(false).ConfigureAwait(false);
            return await ReadStatusAsync(searchService).ConfigureAwait(false);
        }

        return status;
    }

    private static async Task<UsnIndexer.IndexerStatus> WaitForStartupStatusAsync(SearchService searchService)
    {
        using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var token = startupTimeout.Token;
        while (!token.IsCancellationRequested)
        {
            var status = await ReadStatusAsync(searchService, token).ConfigureAwait(false);
            if (status.State != "error") return status;
            try { await Task.Delay(200, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
        return ResolveConnectionStatus(false, null);
    }

    internal static async Task<UsnIndexer.IndexerStatus> ReadStatusAsync(
        SearchService searchService, CancellationToken token = default)
    {
        var (connected, status) = await searchService.GetStatusForReadinessAsync(token).ConfigureAwait(false);
        return ResolveConnectionStatus(connected, status);
    }

    internal static UsnIndexer.IndexerStatus ResolveConnectionStatus(bool connected, UsnIndexer.IndexerStatus? status) =>
        connected && status is { State: not "error" } ? status
            : new UsnIndexer.IndexerStatus { State = connected ? IndexError : "error" };
}
