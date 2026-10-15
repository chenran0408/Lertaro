using Lertaro.Core.IndexV2.Space;
using Lertaro.App.Services;

namespace Lertaro.App.ViewModels.SpaceAnalyzer;

internal sealed class SpaceDisplayItem
{
    public required SpaceIndexEntry Entry { get; init; }
    public SpaceVolumeInfo? Volume { get; init; }
    public bool IsVolume => Volume != null;
    public bool IsIndexed { get; init; } = true;
    public bool OpenInFileManager => IsVolume && (!IsIndexed || Volume?.HasCapacity != true);
    public string CapacityText => Volume == null ? "" : !Volume.HasCapacity
        ? TranslationManager.Instance["Space_VolumeUnavailable"]
        : string.Format(TranslationManager.Instance["Space_VolumeCapacity"],
            SpaceSizeFormatter.Format(Volume.UsedBytes ?? 0), SpaceSizeFormatter.Format(Volume.FreeBytes ?? 0),
            SpaceSizeFormatter.Format(Volume.TotalBytes ?? 0));
    public string IndexStatusText => IsVolume && !IsIndexed ? TranslationManager.Instance["Space_VolumeUnindexed"] : "";
    public string Path => Entry.Path;
    public string Name => Entry.Name;
    public long Size => Entry.Size;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsHardLinkDuplicate => Entry.IsHardLinkDuplicate;
    public double RelativePercentage { get; init; }
    public string DisplaySize => IsVolume && Volume?.HasCapacity != true ? "—" : SpaceSizeFormatter.Format(Size);
    public string ToolTipText => $"{Name}{Environment.NewLine}{(IsVolume ? CapacityText : DisplaySize)}{Environment.NewLine}{IndexStatusText}";
}

internal static class SpaceSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        var value = Math.Max(0, bytes);
        var scaled = (double)value;
        var unit = 0;
        while (scaled >= 1024 && unit < Units.Length - 1)
        {
            scaled /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value} {Units[unit]}" : $"{scaled:0.##} {Units[unit]}";
    }

    public static double RelativePercentage(long bytes, long totalBytes)
        => totalBytes <= 0 ? 0 : Math.Clamp((double)Math.Max(0, bytes) / totalBytes * 100, 0, 100);
}
