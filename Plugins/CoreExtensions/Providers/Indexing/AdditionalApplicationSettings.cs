using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

internal sealed class AdditionalApplication
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Aliases { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
}
internal sealed class AdditionalApplicationFolder { public string Path { get; set; } = ""; }

// Uses the host's staged array editor and file/folder pickers; old multiline folder settings stay valid.
internal static class AdditionalApplicationSettings
{
    internal const string PluginId = "Lertaro.Plugins.CoreExtensions";
    internal const string ProgramsKey = "AdditionalApplications";
    internal const string FoldersKey = "AdditionalApplicationFolders";
    internal static PluginConfigField Config() => new()
    {
        Key = "CustomFoldersGroup", LabelKey = "CoreExtensions_Config_CustomFoldersGroupLabel", FieldType = ConfigFieldType.Group,
        SubFields = Fields().Append(new PluginConfigField
        {
            Key = "CustomFolders", LabelKey = "CoreExtensions_Config_CustomFoldersLabel", DescriptionKey = "CoreExtensions_Config_CustomFoldersDesc",
            FieldType = ConfigFieldType.StringList, DefaultValue = new List<string>(StartMenuAppFolderRoots.DefaultCustomFolders)
        }).ToList()
    };
    internal static IEnumerable<PluginConfigField> Fields() =>
    [
        new() { Key = ProgramsKey, LabelKey = "Applications_Programs", DescriptionKey = "Applications_ProgramsDesc", FieldType = ConfigFieldType.Array, DefaultValue = new List<object>(), SubFields =
        [
            Field("Name", "Applications_Name", ConfigFieldType.Text),
            Field("Path", "Applications_ProgramPath", ConfigFieldType.FilePath),
            Field("Aliases", "Applications_Aliases", ConfigFieldType.Text),
            Field("Arguments", "Applications_Arguments", ConfigFieldType.Text),
            Field("WorkingDirectory", "Applications_WorkingDirectory", ConfigFieldType.FolderPath)
        ]},
        new() { Key = FoldersKey, LabelKey = "Applications_Folders", DescriptionKey = "Applications_FoldersDesc", FieldType = ConfigFieldType.Array, DefaultValue = new List<object>(), SubFields =
        [Field("Path", "Applications_FolderPath", ConfigFieldType.FolderPath)] }
    ];
    private static PluginConfigField Field(string key, string label, ConfigFieldType type) => new()
    {
        Key = key, LabelKey = label, FieldType = type, DefaultValue = "",
        FilePathPattern = key == "Path" && type == ConfigFieldType.FilePath ? "*.exe;*.lnk;*.url;*.appref-ms" : null,
        DescriptionKey = key == "Arguments" ? "Applications_ArgumentsDesc" : ""
    };

    internal static IEnumerable<string> Folders() => PluginSettingsService.GetSetting<List<AdditionalApplicationFolder>>(PluginId, FoldersKey, [])
        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path)).Select(item => item.Path);

    internal static List<SearchableItem> GetItems() => Collect(PluginSettingsService.GetSetting<List<AdditionalApplication>>(PluginId, ProgramsKey, []), File.Exists, RegisteredApplicationDiscovery.GetTitle);

    internal static List<SearchableItem> Collect(IEnumerable<AdditionalApplication> programs, Func<string, bool> exists, Func<string, string?>? title = null)
    {
        var items = new List<SearchableItem>();
        foreach (var program in programs)
        {
            if (program == null || string.IsNullOrWhiteSpace(program.Path)) continue;
            var path = UserPathResolver.Expand(program.Path.Trim().Trim('"'));
            if (!System.IO.Path.IsPathFullyQualified(path) || !StartMenuShortcutResolver.ShouldIndex(path) || !exists(path)) continue;
            // Shortcut files already own their arguments; additional parameters are for executable files.
            if (!System.IO.Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(program.Arguments)) continue;
            var name = string.IsNullOrWhiteSpace(program.Name) ? title?.Invoke(path)?.Trim() is { Length: > 0 } friendly ? friendly : System.IO.Path.GetFileNameWithoutExtension(path) : program.Name.Trim();
            var directory = UserPathResolver.Expand((program.WorkingDirectory ?? "").Trim());
            items.Add(ApplicationCatalogEntries.Create(name, path, program.Arguments ?? "", directory,
                (program.Aliases ?? "").Split([',', '，', ';', '；', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }
        return items;
    }
}
