using Lertaro.App.Services;

namespace Lertaro.App.ViewModels.Settings.Plugins;

public sealed record PluginConfigurationCategory(string Key)
{
    internal static string KeyFor(string assemblyFile) => assemblyFile.ToLowerInvariant() switch
    {
        "lertaro.plugins.websearch.dll" or "lertaro.plugins.browserdata.dll" or "lertaro.plugins.contentsearch.dll"
            or "lertaro.plugins.pinyinalias.dll" or "lertaro.plugins.filefilters.dll" => "Search",
        "lertaro.plugins.customactions.dll" or "lertaro.plugins.customcommands.dll"
            or "lertaro.plugins.flowlauncherbridge.dll" => "Actions",
        "lertaro.plugins.coreextensions.dll" or "lertaro.plugins.windowswitcher.dll" or "lertaro.plugins.processmanager.dll"
            or "lertaro.plugins.taskbarappearance.dll" or "lertaro.plugins.audiodeviceselector.dll" => "Desktop",
        "lertaro.plugins.calendar.dll" or "lertaro.plugins.clipboardhistory.dll" or "lertaro.plugins.translator.dll" => "Tools",
        "lertaro.plugins.directoryopus.dll" or "lertaro.plugins.files.dll" or "lertaro.plugins.onecommander.dll"
            or "lertaro.plugins.totalcommander.dll" or "lertaro.plugins.xyplorer.dll"
            or "lertaro.plugins.tessoa.dll" or "lertaro.plugins.xplorer2.dll" => "FileManagers",
        _ => "Other"
    };

    public string Name => TranslationManager.Instance["Plugins_Category" + Key];
    public override string ToString() => Name;
}
