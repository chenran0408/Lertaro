using System.Text.Json.Nodes;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.TaskbarAppearance.Tests;

[TestClass]
public sealed class TaskbarConfigStoreTests
{
    [TestMethod]
    public void EnableBacksUpOnceAndDisableRestoresSystemAppearance()
    {
        var directory = Directory.CreateTempSubdirectory("LertaroTaskbarTest_");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            const string original = "{\"hide_tray\":true}";
            File.WriteAllText(path, original);
            Assert.IsTrue(TaskbarConfigStore.Apply(path, new() { Enabled = true }));
            Assert.AreEqual(original, File.ReadAllText(path + ".lertaro-backup"));
            TaskbarConfigStore.Apply(path, new() { Enabled = true, Style = "acrylic" });
            Assert.AreEqual(original, File.ReadAllText(path + ".lertaro-backup"));
            TaskbarConfigStore.Apply(path, new());
            var root = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.AreEqual("normal", root["desktop_appearance"]!["accent"]!.GetValue<string>());
            Assert.IsTrue(root["hide_tray"]!.GetValue<bool>());
            Assert.IsFalse(File.Exists(path + ".lertaro-managed"));
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void DisabledOrInvalidSettingsDoNotModifyAnUnmanagedFile()
    {
        var directory = Directory.CreateTempSubdirectory("LertaroTaskbarTest_");
        try
        {
            var path = Path.Combine(directory.FullName, "settings.json");
            const string original = "{\"language\":\"ja-JP\"}";
            File.WriteAllText(path, original);
            Assert.IsFalse(TaskbarConfigStore.Apply(path, new()));
            Assert.ThrowsExactly<ArgumentException>(() => TaskbarConfigStore.Apply(path, new() { Enabled = true, Opacity = -1 }));
            Assert.AreEqual(original, File.ReadAllText(path));
            Assert.HasCount(1, directory.GetFiles());
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void EveryConfigLabelAndChoiceIsTranslatedInEveryShippedLocale()
    {
        var plugin = new TaskbarAppearancePlugin();
        Assert.HasCount(7, plugin.SupportedCultures);
        Assert.IsFalse((bool)plugin.GetConfigSchema().Fields.Single(field => field.Key == "Enabled").DefaultValue);
        foreach (var culture in plugin.SupportedCultures)
        {
            var translations = plugin.GetTranslations(culture);
            Check(plugin.GetConfigSchema().Fields, translations);
        }
    }

    private static void Check(IEnumerable<PluginConfigField> fields, IReadOnlyDictionary<string, string> translations)
    {
        foreach (var field in fields)
        {
            Assert.IsTrue(translations.ContainsKey(field.LabelKey), field.LabelKey);
            if (!string.IsNullOrEmpty(field.DescriptionKey)) Assert.IsTrue(translations.ContainsKey(field.DescriptionKey), field.DescriptionKey);
            foreach (var choice in field.ChoiceOptions ?? []) Assert.IsTrue(translations.ContainsKey(choice.LabelKey), choice.LabelKey);
            Check(field.SubFields ?? [], translations);
        }
    }
}
