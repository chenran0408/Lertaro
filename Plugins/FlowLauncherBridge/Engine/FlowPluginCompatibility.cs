using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

internal static class FlowPluginCompatibility
{
    // Compatibility ceiling, not Lertaro's version and not a promise of complete Flow API support.
    internal const string FlowVersion = "1.19.0";

    internal static string? GetBlockReason(string language, string minimumVersion)
    {
        if (!AllowedLanguage.IsAllowed(language)) return TranslationService.Get("FlowLauncherBridge_UnsupportedLanguage");
        if (!string.IsNullOrWhiteSpace(minimumVersion) &&
            (!Version.TryParse(minimumVersion.TrimStart('v', 'V'), out var minimum) || minimum > new Version(FlowVersion)))
            return TranslationService.Get("FlowLauncherBridge_RequiresNewerFlow");
        return null;
    }
}
