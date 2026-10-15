namespace Lertaro.Core.Hook.InlineSearch;

internal static class QuickPanelMouseWakePolicy
{
    internal static bool Allows(UserSettings settings, bool suspended, bool isDesktop, string? targetProcess,
        string? foregroundProcess, bool fullscreen)
        => Reason(settings, suspended, isDesktop, targetProcess, foregroundProcess, fullscreen) is null;

    internal static string? Reason(UserSettings settings, bool suspended, bool isDesktop, string? targetProcess,
        string? foregroundProcess, bool fullscreen, QuickPanelWakeTestRequest? test = null, bool keyboard = false)
    {
        if (suspended) return "Wake_TestPaused";
        if (!settings.QuickPanel.Enabled) return "Wake_TestDisabled";
        if (!settings.Hotkeys.AllowHotkeysInFullscreen && fullscreen) return "Wake_TestFullscreen";
        if (QuickPanelTabSelection.IsBlocked(targetProcess, settings)
            || QuickPanelTabSelection.IsBlocked(foregroundProcess, settings)) return "Wake_TestBlacklisted";
        if (keyboard) return null;
        return (isDesktop ? test?.AllowDesktop ?? settings.Hotkeys.QuickPanelMouseAllowDesktop
            : ProcessNameFilter.Matches(targetProcess, test?.AllowedProcesses ?? settings.Hotkeys.QuickPanelMouseAllowedProcesses))
            ? null : "Wake_TestLocation";
    }
}
