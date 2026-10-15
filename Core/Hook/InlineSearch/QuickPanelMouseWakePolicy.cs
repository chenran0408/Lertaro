namespace Lertaro.Core.Hook.InlineSearch;

internal static class QuickPanelMouseWakePolicy
{
    internal static bool Allows(UserSettings settings, bool suspended, bool isDesktop, string? targetProcess,
        string? foregroundProcess, bool fullscreen)
    {
        if (suspended || !settings.QuickPanel.Enabled || (!settings.Hotkeys.AllowHotkeysInFullscreen && fullscreen)
            || QuickPanelTabSelection.IsBlocked(targetProcess, settings)
            || QuickPanelTabSelection.IsBlocked(foregroundProcess, settings)) return false;
        return isDesktop ? settings.Hotkeys.QuickPanelMouseAllowDesktop
            : ProcessNameFilter.Matches(targetProcess, settings.Hotkeys.QuickPanelMouseAllowedProcesses);
    }
}
