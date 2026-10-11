using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class ProcessBlacklistEditorViewModelTests
{
    [TestMethod]
    public void SetRunningProcesses_FiltersWindowlessAppsAndSearchesTitles()
    {
        var vm = Create();
        Assert.HasCount(2, vm.RunningProcesses);
        vm.ProcessSearch = "document";
        Assert.AreEqual("editor.exe", vm.RunningProcesses.Single().Name);
        vm.ProcessSearch = "worker";
        Assert.IsEmpty(vm.RunningProcesses);
        vm.ShowAllProcesses = true;
        Assert.AreEqual("worker.exe", vm.RunningProcesses.Single().Name);
    }

    [TestMethod]
    public void AddSelectedProcesses_MergesPendingBulkTextAndDeduplicates()
    {
        var vm = Create();
        vm.BulkText = "EDITOR.EXE\nmanual.exe";
        foreach (var choice in vm.RunningProcesses) choice.IsSelected = true;
        Assert.AreEqual(2, vm.SelectedRunningCount);
        Assert.IsTrue(vm.AddSelectedProcessesCommand.CanExecute(null));
        vm.AddSelectedProcessesCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { "EDITOR.EXE", "manual.exe", "browser.exe" }, vm.ToSettingsList());
        Assert.AreEqual(0, vm.SelectedRunningCount);
        Assert.IsFalse(vm.AddSelectedProcessesCommand.CanExecute(null));
    }

    [TestMethod]
    public void AddSelectedProcesses_RecognizesExistingNamesWithoutExeSuffix()
    {
        var vm = Create();
        vm.BulkText = "EDITOR";
        vm.RunningProcesses.Single(x => x.Name == "editor.exe").IsSelected = true;
        vm.AddSelectedProcessesCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { "EDITOR" }, vm.ToSettingsList());
    }

    [TestMethod]
    public void RefreshAndSearch_PreserveSelectionsByProcessName()
    {
        var vm = Create();
        vm.RunningProcesses.Single(x => x.Name == "editor.exe").IsSelected = true;
        vm.ProcessSearch = "browser";
        Assert.AreEqual(1, vm.SelectedRunningCount);
        vm.SetRunningProcesses([new("EDITOR.exe", "New document", true), new("browser.exe", "Browser", true)]);
        Assert.AreEqual(1, vm.SelectedRunningCount);
        vm.AddSelectedProcessesCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { "EDITOR.exe" }, vm.ToSettingsList());
    }

    [TestMethod]
    public void Refresh_DropsSelectionsForExitedProcesses()
    {
        var vm = Create();
        vm.RunningProcesses[0].IsSelected = true;
        vm.SetRunningProcesses([]);
        Assert.AreEqual(0, vm.SelectedRunningCount);
        Assert.IsEmpty(vm.RunningProcesses);
    }

    private static ProcessBlacklistEditorViewModel Create()
    {
        var vm = new ProcessBlacklistEditorViewModel([]);
        vm.SetRunningProcesses([new("browser.exe", "Browser", true),
            new("editor.exe", "Document", true), new("worker.exe", "", false)]);
        return vm;
    }
}
