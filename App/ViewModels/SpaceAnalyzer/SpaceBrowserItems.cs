using Lertaro.App.Services;
using Lertaro.Core.IndexV2.Space;

namespace Lertaro.App.ViewModels.SpaceAnalyzer;

internal static class SpaceBrowserItems
{
    public static IReadOnlyList<SpaceDisplayItem> Create(IReadOnlyList<SpaceIndexEntry> entries,
        IReadOnlyList<SpaceVolumeInfo>? volumes = null)
    {
        if (volumes == null)
        {
            var total = entries.Aggregate(0L, static (sum, entry) => sum > long.MaxValue - entry.Size ? long.MaxValue : sum + entry.Size);
            return entries.Select(entry => new SpaceDisplayItem
            {
                Entry = entry, RelativePercentage = SpaceSizeFormatter.RelativePercentage(entry.Size, total)
            }).ToList();
        }
        var indexed = entries.ToDictionary(entry => entry.Path.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var items = volumes.Select(volume =>
        {
            var isIndexed = indexed.TryGetValue(volume.Path.TrimEnd('\\', '/'), out var entry);
            var name = string.IsNullOrWhiteSpace(volume.Label) ? volume.Path : $"{volume.Label} ({volume.Path.TrimEnd('\\', '/')})";
            return new SpaceDisplayItem
            {
                Entry = new(volume.Path, name, volume.UsedBytes ?? 0, true, false),
                Volume = volume, IsIndexed = isIndexed,
                RelativePercentage = SpaceSizeFormatter.RelativePercentage(volume.UsedBytes ?? 0, volume.TotalBytes ?? 0)
            };
        }).ToList();
        // Keep explicitly indexed UNC shares; missing local roots are removed even if the service's
        // index still has a disconnected USB volume. Mapped network drives come from the catalog.
        items.AddRange(entries.Where(entry => entry.Path.StartsWith(@"\\", StringComparison.Ordinal))
            .Select(entry => new SpaceDisplayItem { Entry = entry }));
        return items.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
