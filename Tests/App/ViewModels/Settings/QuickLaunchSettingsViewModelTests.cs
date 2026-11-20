using System.IO;
using Lertaro.Core;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class QuickLaunchSettingsViewModelTests
{
    [TestMethod]
    public void ApplyAndSwitchCommitDraftsAndFilteredClearKeepsHiddenRows()
    {
        var settings = new UserSettings();
        settings.QuickLaunch.Items.Clear();
        var vm = new QuickLaunchSettingsViewModel(settings);
        foreach (var name in new[] { "first", "second", "third" })
        {
            vm.NewPath = "https://example.invalid/" + name;
            vm.NewName = name;
            vm.AddCommand.Execute(null);
        }

        var first = vm.Items[0];
        var second = vm.Items[1];
        vm.EditCommand.Execute(first);
        first.EditName = "renamed";
        vm.EditCommand.Execute(second);
        Assert.AreEqual("renamed", first.Name);
        second.EditName = "updated";
        vm.Save();
        Assert.AreEqual("updated", settings.QuickLaunch.Items[1].Name);

        vm.EditCommand.Execute(first);
        first.EditPath = second.Path;
        vm.EditCommand.Execute(second);
        Assert.IsTrue(first.IsEditing);
        Assert.IsTrue(first.HasPathError);
        Assert.IsFalse(second.IsEditing);
        vm.FilterText = "second";
        vm.ClearCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { "renamed", "third" }, vm.Items.Select(row => row.Name).ToArray());
        Assert.IsFalse(vm.ClearCommand.CanExecute(null));
    }

    [TestMethod]
    public void SuggestedNamesFollowTypingUntilTheUserChoosesAName()
    {
        var vm = new QuickLaunchSettingsViewModel(new UserSettings());
        vm.NewPath = @"D:\sample\a";
        vm.NewPath += "bc.exe";
        Assert.AreEqual("abc", vm.NewName);
        vm.NewPath = "https://example.invalid/";
        Assert.AreEqual(string.Empty, vm.NewName);
        vm.NewName = "chosen";
        vm.NewPath = @"D:\sample\different.exe";
        Assert.AreEqual("chosen", vm.NewName);
    }

    [TestMethod]
    public void ShortcutBadges_DefaultToEnabledAndSaveChanges()
    {
        var settings = new UserSettings();
        var vm = new QuickLaunchSettingsViewModel(settings);

        Assert.IsTrue(vm.ShowShortcutBadges);

        vm.ShowShortcutBadges = false;
        vm.Save();

        Assert.IsFalse(settings.QuickLaunch.ShowShortcutBadges);
    }

    [TestMethod]
    public void EditCommand_Execute_StartsInlineEditWithoutRemovingItem()
    {
        var vm = new QuickLaunchSettingsViewModel(new UserSettings());
        var path = Path.GetTempPath();
        vm.NewPath = path;
        vm.AddCommand.Execute(null);
        var item = vm.Items[0];

        vm.EditCommand.Execute(item);

        Assert.HasCount(1, vm.Items);
        Assert.IsTrue(item.IsEditing);
        Assert.AreEqual(item.Name, item.EditName);
        Assert.AreEqual(path, item.EditPath);
    }

    [TestMethod]
    public void ClearCommand_Execute_ClearsItemsAndUpdatesHasItems()
    {
        var vm = new QuickLaunchSettingsViewModel(new UserSettings()) { NewPath = Path.GetTempPath() };
        vm.AddCommand.Execute(null);

        Assert.IsTrue(vm.HasItems);

        vm.ClearCommand.Execute(null);

        Assert.IsEmpty(vm.Items);
        Assert.IsFalse(vm.HasItems);
    }

    [TestMethod]
    public void AddPaths_AddsAllUniqueExistingPathsWithAutomaticNames()
    {
        // Real system executables instead of fixture files: creating an *.exe/*.cmd in the temp
        // directory trips antivirus real-time protection on some machines (UnauthorizedAccessException
        // on CreateFile), and this test only needs paths that EXIST. The automatic-name behavior under
        // test is extension-driven (known executable extensions get hidden), which System32 binaries
        // exercise identically.
        var first = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var second = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var vm = new QuickLaunchSettingsViewModel(new UserSettings());

        vm.AddPaths(new[] { first, second, first });

        Assert.HasCount(2, vm.Items);
        Assert.AreEqual(Path.GetFileNameWithoutExtension(first), vm.Items[0].Name);
        Assert.AreEqual(Path.GetFileNameWithoutExtension(second), vm.Items[1].Name);
    }
}
