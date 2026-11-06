using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory.Tests;

// PluginSettingsService.GetSettingFunc is a shared static delegate -- [DoNotParallelize] plus
// wiring/reset in TestInitialize/TestCleanup keeps tests in this class from racing on it against
// other tests in the assembly, including a prior run that may have left stale state behind.
[TestClass]
[DoNotParallelize]
public sealed class ClipboardSettingsTests
{
    private Dictionary<string, object?> _settings = new();

    [TestInitialize]
    public void WireFakeSettings()
    {
        _settings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == ClipboardSettings.PluginId
                && _settings.TryGetValue(key, out var value)
                && value is not null
                ? value
                : defaultValue;
    }

    [TestCleanup]
    public void Reset() => PluginSettingsService.GetSettingFunc = null;

    [TestMethod]
    public void Getters_NothingConfigured_ReturnSpecDefaults()
    {
        Assert.IsTrue(ClipboardSettings.Enabled);
        Assert.AreEqual("cb", ClipboardSettings.TriggerKeyword);
        Assert.AreEqual(500, ClipboardSettings.MaxEntries);
        Assert.AreEqual(1000000, ClipboardSettings.MaxTextLength);
        Assert.AreEqual("keepass,keepassxc,1password,bitwarden,enpass", ClipboardSettings.ExcludedProcesses);
    }

    [TestMethod]
    public void Getters_ConfiguredValues_AreReturnedLive()
    {
        _settings["Enabled"] = false;
        _settings["TriggerKeyword"] = "  clip ";
        _settings["MaxEntries"] = 250;
        _settings["MaxTextLength"] = 12345;
        _settings["ExcludedProcesses"] = "keepass";

        Assert.IsFalse(ClipboardSettings.Enabled);
        Assert.AreEqual("clip", ClipboardSettings.TriggerKeyword);
        Assert.AreEqual(250, ClipboardSettings.MaxEntries);
        Assert.AreEqual(12345, ClipboardSettings.MaxTextLength);
        Assert.AreEqual("keepass", ClipboardSettings.ExcludedProcesses);
    }

    [TestMethod]
    public void TriggerKeyword_EmptyOrWhitespaceConfigured_FallsBackToDefault()
    {
        _settings["TriggerKeyword"] = "   ";
        Assert.AreEqual("cb", ClipboardSettings.TriggerKeyword);

        _settings["TriggerKeyword"] = string.Empty;
        Assert.AreEqual("cb", ClipboardSettings.TriggerKeyword);
    }

    [TestMethod]
    public void MaxEntries_ZeroOrNegative_ClampedToOne()
    {
        _settings["MaxEntries"] = 0;
        Assert.AreEqual(1, ClipboardSettings.MaxEntries);

        _settings["MaxEntries"] = -50;
        Assert.AreEqual(1, ClipboardSettings.MaxEntries);
    }

    [TestMethod]
    public void MaxTextLength_ClampedBetweenOneAndCap()
    {
        _settings["MaxTextLength"] = 0;
        Assert.AreEqual(1, ClipboardSettings.MaxTextLength);

        _settings["MaxTextLength"] = -100;
        Assert.AreEqual(1, ClipboardSettings.MaxTextLength);

        _settings["MaxTextLength"] = 5000000;
        Assert.AreEqual(1000000, ClipboardSettings.MaxTextLength);
    }

    [TestMethod]
    public void CreateSchema_DeclaresAllFieldsWithExpectedTypesAndDefaults()
    {
        var fields = ClipboardSettings.CreateSchema().Fields;

        Assert.HasCount(8, fields);
        Assert.AreEqual(ConfigFieldType.Boolean, Field(fields, "Enabled").FieldType);
        Assert.AreEqual(true, Field(fields, "Enabled").DefaultValue);
        Assert.AreEqual(ConfigFieldType.Text, Field(fields, "TriggerKeyword").FieldType);
        Assert.AreEqual("cb", Field(fields, "TriggerKeyword").DefaultValue);
        Assert.AreEqual(ConfigFieldType.Integer, Field(fields, "MaxEntries").FieldType);
        Assert.AreEqual(500, Field(fields, "MaxEntries").DefaultValue);
        Assert.AreEqual(ConfigFieldType.Integer, Field(fields, "MaxTextLength").FieldType);
        Assert.AreEqual(1000000, Field(fields, "MaxTextLength").DefaultValue);
        Assert.AreEqual(ConfigFieldType.Text, Field(fields, "ExcludedProcesses").FieldType);
        Assert.AreEqual("keepass,keepassxc,1password,bitwarden,enpass", Field(fields, "ExcludedProcesses").DefaultValue);
        Assert.AreEqual(ConfigFieldType.Choice, Field(fields, "RetentionDays").FieldType);
        Assert.AreEqual("30", Field(fields, "RetentionDays").DefaultValue);
    }

    [TestMethod]
    public void CreateSchema_TriggerKeywordIsTriggerWordAndRequiredNonEmpty()
    {
        var fields = ClipboardSettings.CreateSchema().Fields;

        Assert.IsTrue(Field(fields, "TriggerKeyword").IsTriggerWord);
        Assert.IsTrue(Field(fields, "TriggerKeyword").RequireNonEmpty);
        Assert.IsFalse(Field(fields, "Enabled").IsTriggerWord);
    }

    [TestMethod]
    public void CreateSchema_HasNoRuntimeSaveHooks()
    {
        var schema = ClipboardSettings.CreateSchema();

        Assert.IsNull(schema.OnSave);
        Assert.IsNull(schema.OnSaveAsync);
        Assert.IsNull(schema.OnPreviewAsync);
        Assert.IsNull(schema.OnRollback);
    }

    private static PluginConfigField Field(List<PluginConfigField> fields, string key) =>
        fields.Single(f => f.Key == key);
}
