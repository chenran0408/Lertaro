using System.Reflection;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.TaskbarAppearance;

public sealed class TaskbarAppearancePlugin : IPlugin, IConfigurable, ITranslationProvider
{
    private const string PluginId = "Lertaro.Plugins.TaskbarAppearance";
    public string Name => TranslationService.Get("Taskbar_Name");
    public string Description => TranslationService.Get("Taskbar_Description");
    public string WebsiteUrl => "https://github.com/TranslucentTB/TranslucentTB";
    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(Assembly.GetExecutingAssembly());
    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
        => TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureName, "Plugin");

    public PluginConfigSchema GetConfigSchema() => new()
    {
        Fields = [
            Field("Enabled", ConfigFieldType.Boolean, false),
            Field("ExecutablePath", ConfigFieldType.FilePath, ""),
            StyleField("clear"),
            Field("Color", ConfigFieldType.Text, "#000000"),
            Field("Opacity", ConfigFieldType.Integer, 0),
            Field("ShowLine", ConfigFieldType.Boolean, false),
            Field("ShowPeek", ConfigFieldType.Boolean, true),
            Field("UseChinese", ConfigFieldType.Boolean, true),
            new PluginConfigField { Key = "Rules", LabelKey = "Taskbar_Rules", FieldType = ConfigFieldType.Array,
                DefaultValue = new List<object>(), SubFields = [
                    Field("Enabled", ConfigFieldType.Boolean, true),
                    new PluginConfigField { Key = "Situation", LabelKey = "Taskbar_Situation", FieldType = ConfigFieldType.Choice,
                        DefaultValue = TaskbarConfiguration.Situations[1], ChoiceOptions = TaskbarConfiguration.Situations.Select(value =>
                            new PluginConfigChoice { Value = value, LabelKey = "Taskbar_" + value }).ToList() },
                    StyleField("normal"), Field("Color", ConfigFieldType.Text, "#000000"), Field("Opacity", ConfigFieldType.Integer, 0)
                ] }
        ],
        OnSave = Save
    };

    private static void Save()
    {
        try { TaskbarEngine.Apply(ReadOptions()); }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[TaskbarAppearance] {ex.Message}", PluginSdk.LogLevel.Error);
            throw new InvalidOperationException(TranslationService.Get("Taskbar_ApplyFailed"), ex);
        }
    }

    private static PluginConfigField Field(string key, ConfigFieldType type, object value) => new()
    {
        Key = key, LabelKey = "Taskbar_" + key, DescriptionKey = "Taskbar_" + key + "Hint", FieldType = type,
        DefaultValue = value, FilePathPattern = type == ConfigFieldType.FilePath ? "TranslucentTB.exe" : null
    };

    private static PluginConfigField StyleField(string value) => new()
    {
        Key = "Style", LabelKey = "Taskbar_Style", FieldType = ConfigFieldType.Choice, DefaultValue = value,
        DescriptionKey = "Taskbar_StyleHint",
        ChoiceOptions = TaskbarConfiguration.Styles.Select(style => new PluginConfigChoice { Value = style, LabelKey = "Taskbar_" + style }).ToList()
    };

    private static TaskbarOptions ReadOptions() => new()
    {
        Enabled = PluginSettingsService.GetSetting(PluginId, "Enabled", false),
        ExecutablePath = PluginSettingsService.GetSetting(PluginId, "ExecutablePath", ""),
        Style = PluginSettingsService.GetSetting(PluginId, "Style", "clear"),
        Color = PluginSettingsService.GetSetting(PluginId, "Color", "#000000"),
        Opacity = PluginSettingsService.GetSetting(PluginId, "Opacity", 0),
        ShowLine = PluginSettingsService.GetSetting(PluginId, "ShowLine", false),
        ShowPeek = PluginSettingsService.GetSetting(PluginId, "ShowPeek", true),
        UseChinese = PluginSettingsService.GetSetting(PluginId, "UseChinese", true),
        Rules = PluginSettingsService.GetSetting(PluginId, "Rules", new List<TaskbarRule>())
    };
}
