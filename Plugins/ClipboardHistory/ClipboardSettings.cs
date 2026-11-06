using Lertaro.PluginSdk;
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
    private const string DefaultRetentionDays = "30";
    private const string DefaultImageRetentionDays = "14";
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

    /// <summary>Days an unpinned entry survives; 0 keeps everything. Only the whitelisted values the
    /// schema offers are honored, so a hand-edited value falls back to the default rather than
    /// silently switching retention off.</summary>
    public static int RetentionDays
    {
        get
        {
            var raw = PluginSettingsService.GetSetting(PluginId, "RetentionDays", DefaultRetentionDays)?.ToString();
            return raw is "1" or "7" or "30" or "0" ? int.Parse(raw) : int.Parse(DefaultRetentionDays);
        }
    }

    /// <summary>Days an unpinned image survives; 0 keeps everything. Images are the disk-hog
    /// kind, so they get their own window -- screenshots age out faster than text by default.</summary>
    public static int ImageRetentionDays
    {
        get
        {
            var raw = PluginSettingsService.GetSetting(PluginId, "ImageRetentionDays", DefaultImageRetentionDays)?.ToString();
            return raw is "1" or "7" or "14" or "30" or "0" ? int.Parse(raw) : int.Parse(DefaultImageRetentionDays);
        }
    }

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
                Key = "RetentionDays",
                LabelKey = "ClipboardHistory_Config_RetentionLabel",
                DescriptionKey = "ClipboardHistory_Config_RetentionDesc",
                FieldType = ConfigFieldType.Choice,
                DefaultValue = DefaultRetentionDays,
                ChoiceOptions =
                [
                    new PluginConfigChoice { Value = "1", LabelKey = "ClipboardHistory_Config_Retention1" },
                    new PluginConfigChoice { Value = "7", LabelKey = "ClipboardHistory_Config_Retention7" },
                    new PluginConfigChoice { Value = "14", LabelKey = "ClipboardHistory_Config_Retention14" },
                    new PluginConfigChoice { Value = "30", LabelKey = "ClipboardHistory_Config_Retention30" },
                    new PluginConfigChoice { Value = "0", LabelKey = "ClipboardHistory_Config_RetentionForever" }
                ]
            },
            new PluginConfigField
            {
                Key = "ImageRetentionDays",
                LabelKey = "ClipboardHistory_Config_ImageRetentionLabel",
                DescriptionKey = "ClipboardHistory_Config_ImageRetentionDesc",
                FieldType = ConfigFieldType.Choice,
                DefaultValue = DefaultImageRetentionDays,
                ChoiceOptions =
                [
                    new PluginConfigChoice { Value = "1", LabelKey = "ClipboardHistory_Config_Retention1" },
                    new PluginConfigChoice { Value = "7", LabelKey = "ClipboardHistory_Config_Retention7" },
                    new PluginConfigChoice { Value = "14", LabelKey = "ClipboardHistory_Config_Retention14" },
                    new PluginConfigChoice { Value = "30", LabelKey = "ClipboardHistory_Config_Retention30" },
                    new PluginConfigChoice { Value = "0", LabelKey = "ClipboardHistory_Config_RetentionForever" }
                ]
            },
            new PluginConfigField
            {
                Key = "ExcludedProcesses",
                LabelKey = "ClipboardHistory_Config_ExcludedProcessesLabel",
                DescriptionKey = "ClipboardHistory_Config_ExcludedProcessesDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = DefaultExcludedProcesses
            },
            // Win+V is owned by Windows' own clipboard history while that feature is on, so the
            // wake hotkey can dead-fire. This button sends the user to the one switch that frees
            // the combination; the plugin panel replaces everything the system panel did.
            new PluginConfigField
            {
                Key = "OpenSystemClipboardSettings",
                LabelKey = "ClipboardHistory_Config_SystemClipboardLabel",
                DescriptionKey = "ClipboardHistory_Config_SystemClipboardDesc",
                FieldType = ConfigFieldType.Button,
                DefaultValue = string.Empty,
                OnClick = OpenSystemClipboardSettings
            }
        ]
    };

    internal static void OpenSystemClipboardSettings()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("ms-settings:clipboard") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.FileNotFoundException)
        {
            Logger.Log($"[ClipboardHistory] Could not open ms-settings:clipboard: {ex.GetType().Name}", LogLevel.Warn);
        }
    }
}
