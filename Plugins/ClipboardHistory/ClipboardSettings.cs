using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory;

// Central read point for every ClipboardHistory setting. All getters read live through
// PluginSettingsService on each call (no cached copies), so the runtime's SettingChanged
// subscription sees new values on the next read without this class keeping any state.
internal static class ClipboardSettings
{
    public const string PluginId = "Lertaro.Plugins.ClipboardHistory";

    private const string DefaultTriggerKeyword = "cb";
    private const string DefaultExcludedProcesses = "keepass,keepassxc,1password,bitwarden,enpass";
    private const int DefaultMaxEntries = 500;
    private const int DefaultMaxTextLength = 1000000;

    public static bool Enabled => PluginSettingsService.GetSetting(PluginId, "Enabled", true);

    // Same defensive pattern as WindowSwitcher's GetTriggerKeyword: the host strips only words
    // providers publish, so an empty value must fall back to the default instead of silently
    // making the trigger unreachable (also covers values persisted before RequireNonEmpty).
    public static string TriggerKeyword
    {
        get
        {
            var value = PluginSettingsService.GetSetting(PluginId, "TriggerKeyword", DefaultTriggerKeyword);
            return TriggerWord.Normalize(value) is { Length: > 0 } word ? word : DefaultTriggerKeyword;
        }
    }

    public static int MaxEntries =>
        Math.Max(1, PluginSettingsService.GetSetting(PluginId, "MaxEntries", DefaultMaxEntries));

    public static int MaxTextLength => Math.Clamp(
        PluginSettingsService.GetSetting(PluginId, "MaxTextLength", DefaultMaxTextLength),
        1, DefaultMaxTextLength);

    public static string ExcludedProcesses =>
        PluginSettingsService.GetSetting(PluginId, "ExcludedProcesses", DefaultExcludedProcesses);

    // No OnSave/OnSaveAsync/OnPreviewAsync hooks here: runtime components observe
    // PluginSettingsService.SettingChanged directly, so the schema only describes fields.
    public static PluginConfigSchema CreateSchema() => new()
    {
        Fields =
        [
            new PluginConfigField
            {
                Key = "Enabled",
                LabelKey = "ClipboardHistory_Config_EnabledLabel",
                DescriptionKey = "ClipboardHistory_Config_EnabledDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            },
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                IsTriggerWord = true,
                LabelKey = "ClipboardHistory_Config_TriggerKeywordLabel",
                DescriptionKey = "ClipboardHistory_Config_TriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = DefaultTriggerKeyword,
                RequireNonEmpty = true
            },
            new PluginConfigField
            {
                Key = "MaxEntries",
                LabelKey = "ClipboardHistory_Config_MaxEntriesLabel",
                DescriptionKey = "ClipboardHistory_Config_MaxEntriesDesc",
                FieldType = ConfigFieldType.Integer,
                DefaultValue = DefaultMaxEntries
            },
            new PluginConfigField
            {
                Key = "MaxTextLength",
                LabelKey = "ClipboardHistory_Config_MaxTextLengthLabel",
                DescriptionKey = "ClipboardHistory_Config_MaxTextLengthDesc",
                FieldType = ConfigFieldType.Integer,
                DefaultValue = DefaultMaxTextLength
            },
            new PluginConfigField
            {
                Key = "ExcludedProcesses",
                LabelKey = "ClipboardHistory_Config_ExcludedProcessesLabel",
                DescriptionKey = "ClipboardHistory_Config_ExcludedProcessesDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = DefaultExcludedProcesses
            }
        ]
    };
}
