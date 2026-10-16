using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

internal static class FlowCommunityRecommendations
{
    // 2026-10-16, Windows x64: official release download, install, query and uninstall tested.
    // Clipboard actions and other features were not executed. Never carry the badge to a new version.
    private static readonly Dictionary<string, (string Version, string Hash, string DescriptionKey)> Tested = new(StringComparer.OrdinalIgnoreCase)
    {
        ["84ddc2a6d43911e8a8d5f2801f1b9fd1"] = ("1.0.8", "CC39E89AF427732118B7E2C393DA84A16E6D349F6F1F6A31DDE48B9983F14F00", "Timestamp"),
        ["959A3FED8292422784F7892F7FB59DB1"] = ("1.0.0", "39B8040F35041904CA1014BA4E28D7565B133DE01A21650CF5549EA9177B2F19", "Uuid"),
        ["e375c983-bdea-4b5f-9325-ea71e44c1a08"] = ("1.0.1", "1139C32EAC461D4B7D8003738CD1C12806129094F6B18E6DBEE0C3B69B0A28BE", "SmartTimestamp")
    };

    internal static string? GetArchiveHash(FlowCommunityPlugin plugin) =>
        !string.IsNullOrWhiteSpace(plugin.ID) && Tested.TryGetValue(plugin.ID, out var tested) && plugin.Version == tested.Version ? tested.Hash : null;

    internal static bool IsSearchTested(FlowCommunityPlugin plugin, Architecture? architecture = null) =>
        (architecture ?? RuntimeInformation.ProcessArchitecture) == Architecture.X64 && GetArchiveHash(plugin) != null;

    internal static string Description(FlowCommunityPlugin plugin) => !string.IsNullOrWhiteSpace(plugin.ID) && Tested.TryGetValue(plugin.ID, out var tested)
        ? TranslationService.Get("FlowLauncherBridge_Recommend" + tested.DescriptionKey)
        : plugin.Description;

    internal static string Status(FlowCommunityPlugin plugin) => TranslationService.Get(IsSearchTested(plugin)
        ? "FlowLauncherBridge_BasicSearchTested" : "FlowLauncherBridge_CompatibilityUnverified");
}
