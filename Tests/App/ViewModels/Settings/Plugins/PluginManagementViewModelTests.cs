using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginManagementViewModelTests
{
    [STATestMethod]
    public void InstalledFilters_CombineSearchWithEnablementAndRevealSelectedDetails()
    {
        var model = new PluginManagementViewModel(new UserSettings());
        try
        {
            model.Plugins.Clear();
            var calendar = new PluginInfoViewModel("Calendar", "1", "calendar.dll", "1", [], [], "Date and time");
            var preview = new PluginInfoViewModel("Preview", "1", "preview.dll", "1", [], [], "File previews", isPluginEnabled: false);
            model.Plugins.Add(calendar);
            model.Plugins.Add(preview);

            model.InstalledPluginSearchText = " DATE ";
            CollectionAssert.AreEqual(new[] { calendar }, model.VisiblePlugins.Cast<PluginInfoViewModel>().ToArray());
            model.InstalledPluginFilter = "Disabled";
            Assert.AreEqual(0, model.VisiblePlugins.Count);
            model.InstalledPluginSearchText = "PREVIEW.DLL";
            CollectionAssert.AreEqual(new[] { preview }, model.VisiblePlugins.Cast<PluginInfoViewModel>().ToArray());

            model.RevealPluginDetails(calendar);
            Assert.AreSame(calendar, model.SelectedPlugin);
            Assert.AreEqual(2, model.VisiblePlugins.Count);
            Assert.IsTrue(model.IsPluginDetailsOpen);
        }
        finally
        {
            model.Cleanup();
        }
    }

    [STATestMethod]
    public void ConfigurationDirectory_ShowsOnlyConfigurablePluginsAndKeepsEachSearch()
    {
        var settings = new UserSettings();
        var model = new PluginManagementViewModel(settings);
        try
        {
            model.Plugins.Clear();
            var config = new PluginConfigFieldViewModel("Web", new() { Key = "keyword" }, settings);
            var web = new PluginInfoViewModel("Web", "1", "web.dll", "1", [], [config]);
            model.Plugins.Add(web);
            model.Plugins.Add(new PluginInfoViewModel("Preview", "1", "preview.dll", "1", [], [], isPluginEnabled: false));
            model.InstalledPluginFilter = "Disabled";
            model.InstalledPluginSearchText = "preview";
            model.IsConfigurationDirectory = true;
            CollectionAssert.AreEqual(new[] { web }, model.VisiblePlugins.Cast<PluginInfoViewModel>().ToArray());
            model.RevealPluginDetails(web);
            model.InstalledPluginSearchText = "web";
            model.IsConfigurationDirectory = false;
            Assert.IsFalse(model.IsPluginDetailsOpen);
            Assert.AreEqual("Disabled", model.InstalledPluginFilter);
            Assert.AreEqual("preview", model.InstalledPluginSearchText);
            model.IsConfigurationDirectory = true;
            Assert.AreEqual("web", model.InstalledPluginSearchText);
        }
        finally
        {
            model.Cleanup();
        }
    }

    [STATestMethod]
    public void DirectoryGroupsByFunctionAndFiltersByLocalizedCategory()
    {
        var settings = new UserSettings();
        var model = new PluginManagementViewModel(settings);
        try
        {
            model.Plugins.Clear();
            PluginInfoViewModel Plugin(string dll) => new("Fixture", "1", dll, "1", [],
                [new PluginConfigFieldViewModel("fixture", new() { Key = "v" }, settings)]);
            var files = Plugin("Lertaro.Plugins.Files.dll");
            var one = Plugin("Lertaro.Plugins.OneCommander.dll");
            var other = Plugin("ThirdParty.dll");
            foreach (var item in new[] { files, one, other }) model.Plugins.Add(item);
            model.IsConfigurationDirectory = true;
            Assert.HasCount(2, model.VisiblePlugins.Groups!);
            model.SelectedCategory = "FileManagers";
            Assert.HasCount(2, model.VisiblePlugins);
            model.SelectedCategory = "Other";
            Assert.AreSame(other, model.SelectedPlugin);
            model.RevealPluginDetails(files);
            Assert.AreEqual("All", model.SelectedCategory);
            model.InstalledPluginSearchText = files.ConfigurationCategory;
            CollectionAssert.AreEquivalent(new[] { files, one }, model.VisiblePlugins.Cast<PluginInfoViewModel>().ToArray());
            Assert.HasCount(1, model.VisiblePlugins.Groups!);
            model.IsConfigurationDirectory = false;
            Assert.IsNull(model.VisiblePlugins.Groups);
            Assert.AreEqual(3, model.VisiblePlugins.Count);
        }
        finally { model.Cleanup(); }
    }

    [TestMethod]
    public void LanguageRefresh_KeepsDraftUntilApplyOrCancel()
    {
        var settings = new UserSettings();
        PluginInfoViewModel Create() => new("P", "1", "P.dll", "1", [],
            [new PluginConfigFieldViewModel("P", new() { Key = "word", FieldType = PluginSdk.Abstractions.ConfigFieldType.Text, DefaultValue = "old" }, settings)]);
        var previous = Create();
        previous.IsConfigTab = true;
        previous.ConfigFields[0].Value = "new";
        var replacement = Create();
        PluginManagementViewModel.RestoreEnablementEdits([previous], [replacement]);
        Assert.IsTrue(replacement.IsConfigTab);
        Assert.AreEqual("new", replacement.ConfigFields[0].Value);
        Assert.IsTrue(replacement.HasPendingConfigEdits);
        replacement.RollbackConfig();
        Assert.AreEqual("old", replacement.ConfigFields[0].Value);
    }

    [TestMethod]
    public void LanguageRefresh_PreservesIndividualFeatureChoices()
    {
        var first = new PluginComponentViewModel("P::Action::One", PluginComponentType.Action, "One", true);
        var second = new PluginComponentViewModel("P::Action::Two", PluginComponentType.Action, "Two", true);
        var edited = new PluginInfoViewModel("P", "1", "P.dll", "1", [first, second], []);
        first.IsEnabled = false;
        var replacement = new PluginInfoViewModel("P", "1", "P.dll", "1",
            [new(first.ComponentId, PluginComponentType.Action, "One", true), new(second.ComponentId, PluginComponentType.Action, "Two", true)], []);
        PluginManagementViewModel.RestoreEnablementEdits([edited], [replacement]);
        Assert.IsFalse(replacement.RawComponents[0].IsEnabled);
        Assert.IsTrue(replacement.RawComponents[1].IsEnabled);
        Assert.IsTrue(replacement.IsPluginEnabled);
    }

    [TestMethod]
    public void Save_AllFeaturesOff_BatchesTwentyFiveWholePlugins()
    {
        var plugins = Enumerable.Range(0, 25).Select(i => new PluginInfoViewModel("P", "1", $"p{i}.dll", "1",
            [new($"p{i}.dll::Action::One", PluginComponentType.Action, "One", true)], [])).ToList();
        foreach (var plugin in plugins) plugin.RawComponents[0].IsEnabled = false;
        var settings = new UserSettings();
        PluginManagementViewModel.SavePluginEnablement(settings, plugins);
        Assert.HasCount(25, settings.DisabledPluginAssemblies);
        Assert.IsTrue(plugins.All(p => !p.IsPluginEnabled));
        plugins[0].RawComponents[0].IsEnabled = true;
        PluginManagementViewModel.SavePluginEnablement(settings, plugins);
        Assert.HasCount(24, settings.DisabledPluginAssemblies);
        Assert.IsTrue(plugins[0].IsPluginEnabled);
    }

    [TestMethod]
    public void LanguageRefresh_PreservesPendingSwitchWithoutOverwritingUntouchedPluginState()
    {
        var edited = new PluginInfoViewModel("A", "1", "a.dll", "1", [], []);
        var untouched = new PluginInfoViewModel("B", "1", "B.dll", "1", [], []);
        edited.IsPluginEnabled = false;
        var replacement = new PluginInfoViewModel("Translated A", "1", "A.DLL", "1", [], []);
        var independentlyDisabled = new PluginInfoViewModel("Translated B", "1", "B.dll", "1", [], [], isPluginEnabled: false);

        PluginManagementViewModel.RestoreEnablementEdits([edited, untouched], [replacement, independentlyDisabled]);

        Assert.IsFalse(replacement.IsPluginEnabled);
        Assert.IsTrue(replacement.IsPluginEnablementDirty);
        Assert.IsFalse(independentlyDisabled.IsPluginEnabled);
        Assert.IsFalse(independentlyDisabled.IsPluginEnablementDirty);
    }

    [TestMethod]
    public void Save_MergesOnlyEditedPluginsAndPreservesUnloadedPluginConfiguration()
    {
        var settings = new UserSettings
        {
            DisabledPluginAssemblies = ["a.DLL", "absent.dll"],
            DisabledPluginComponents = ["A.dll::Action::One"]
        };
        settings.PluginSettings["A"] = new() { ["secret"] = "preserved" };
        var enabled = new PluginInfoViewModel("A", "1", "A.dll", "1", [], [], isPluginEnabled: false);
        var disabled = new PluginInfoViewModel("B", "1", "B.dll", "1", [], []);
        var untouched = new PluginInfoViewModel("Absent", "1", "absent.dll", "1", [], []);
        enabled.IsPluginEnabled = true;
        disabled.IsPluginEnabled = false;
        PluginManagementViewModel.SavePluginEnablement(settings, [enabled, disabled, untouched]);
        CollectionAssert.AreEquivalent(new[] { "absent.dll", "B.dll" }, settings.DisabledPluginAssemblies);
        Assert.AreEqual("preserved", settings.PluginSettings["A"]["secret"]);
        Assert.IsEmpty(settings.DisabledPluginComponents);
    }
}
