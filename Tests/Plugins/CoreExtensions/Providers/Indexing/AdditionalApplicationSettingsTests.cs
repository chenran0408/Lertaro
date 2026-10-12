using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Providers.Indexing;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Indexing;

[TestClass]
public sealed class AdditionalApplicationSettingsTests
{
    [TestMethod]
    public void PortableProgramsKeepNamesAliasesArgumentsAndWorkingFolders()
    {
        var item = Assert.ContainsSingle(AdditionalApplicationSettings.Collect([new() { Name = "Work editor", Path = @"D:\Portable\Editor.exe", Aliases = "edit,工作；work", Arguments = "--profile work", WorkingDirectory = @"D:\Projects" }], _ => true));
        Assert.AreEqual("Work editor", item.Title);
        CollectionAssert.AreEqual(new[] { "edit", "工作", "work" }, item.Keywords.ToArray());
        Assert.AreEqual("--profile work", item.LaunchArguments);
        Assert.AreEqual(@"D:\Projects", item.LaunchWorkingDirectory);
        Assert.IsNotNull(item.OnExecute);
    }

    [TestMethod]
    public void BlankNameUsesFriendlyTitleThenExecutableStem()
    {
        var input = new[] { new AdditionalApplication { Path = @"C:\Portable\program.exe" } };
        Assert.AreEqual("My program", AdditionalApplicationSettings.Collect(input, _ => true, _ => "My program")[0].Title);
        Assert.AreEqual("program", AdditionalApplicationSettings.Collect(input, _ => true)[0].Title);
    }

    [TestMethod]
    public void MissingRelativeAndUnsupportedTargetsAreIgnored()
    {
        var items = AdditionalApplicationSettings.Collect([new() { Path = "program.exe" }, new() { Path = @"C:\Missing.exe" }, new() { Path = @"C:\readme.txt" }], path => !path.Contains("Missing"));
        Assert.IsEmpty(items);
    }

    [TestMethod]
    public void NonExecutableShortcutCannotSilentlyDiscardExtraArguments() =>
        Assert.IsEmpty(AdditionalApplicationSettings.Collect([new() { Path = @"C:\Links\Editor.lnk", Arguments = "--work" }], _ => true));

    [TestMethod]
    public void SettingsUseFileAndFolderPickersWhileKeepingOldFolderList()
    {
        var fields = AdditionalApplicationSettings.Config().SubFields!;
        var programs = fields.Single(field => field.Key == AdditionalApplicationSettings.ProgramsKey);
        Assert.AreEqual(ConfigFieldType.FilePath, programs.SubFields!.Single(field => field.Key == "Path").FieldType);
        Assert.AreEqual(ConfigFieldType.FolderPath, fields.Single(field => field.Key == AdditionalApplicationSettings.FoldersKey).SubFields![0].FieldType);
        Assert.AreEqual(ConfigFieldType.StringList, fields.Single(field => field.Key == "CustomFolders").FieldType);
    }
}
