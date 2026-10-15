using Lertaro.App.Services;
using Lertaro.App.ViewModels.SpaceAnalyzer;
using Lertaro.Core.IndexV2.Space;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.Views.SpaceAnalyzer;

internal static class SpaceBrowserLoader
{
    public static async Task<IReadOnlyList<SpaceDisplayItem>> LoadAsync(SearchService service, string? path, CancellationToken token)
    {
        if (!string.IsNullOrEmpty(path))
            return SpaceBrowserItems.Create(await service.GetSpaceEntriesAsync(path, token));
        var volumesTask = SpaceVolumeCatalog.ReadAsync(token);
        var entriesTask = LoadIndexedRootsAsync(service, token);
        await Task.WhenAll(volumesTask, entriesTask);
        return SpaceBrowserItems.Create(await entriesTask, await volumesTask);
    }

    private static async Task<IReadOnlyList<SpaceIndexEntry>> LoadIndexedRootsAsync(SearchService service, CancellationToken token)
    {
        try { return await service.GetSpaceEntriesAsync(null, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Core.Logger.Log($"[SpaceAnalyzer] Index roots unavailable; showing physical volumes: {ex.Message}", Core.LogLevel.Warn);
            return [];
        }
    }
}
