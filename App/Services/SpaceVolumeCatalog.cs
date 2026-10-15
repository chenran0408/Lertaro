using System.IO;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Services;

internal sealed record SpaceVolumeInfo(string Path, string Label, long? TotalBytes, long? FreeBytes, bool IsRemovable)
{
    public long? UsedBytes => TotalBytes is >= 0 and { } total && FreeBytes is >= 0 and { } free
        ? Math.Max(0, total - Math.Clamp(free, 0, total)) : null;
    public bool HasCapacity => TotalBytes > 0 && FreeBytes >= 0;
}

internal static class SpaceVolumeCatalog
{
    // Capacity probes run off the dispatcher with one shared budget. A disconnected device must not
    // delay healthy drives or consume the thread pool used by search and shortcuts.
    public static Task<IReadOnlyList<SpaceVolumeInfo>> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var roots = DriveInfo.GetDrives()
            .Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram or DriveType.Network)
            .ToArray();
        var probes = roots.Select(drive => (Func<SpaceVolumeInfo?>)(() => Read(drive))).ToArray();
        var answers = BudgetedProbeBatch.Run(probes, null, BudgetedProbeBatch.DefaultBudget);
        token.ThrowIfCancellationRequested();
        return (IReadOnlyList<SpaceVolumeInfo>)roots.Select((drive, index) => answers[index]
            ?? new SpaceVolumeInfo(drive.Name, "", null, null, drive.DriveType == DriveType.Removable)).ToArray();
    }, token);

    private static SpaceVolumeInfo Read(DriveInfo drive)
    {
        if (!drive.IsReady)
            return new(drive.Name, "", null, null, drive.DriveType == DriveType.Removable);
        return new(drive.Name, drive.VolumeLabel, drive.TotalSize, drive.TotalFreeSpace, drive.DriveType == DriveType.Removable);
    }
}
