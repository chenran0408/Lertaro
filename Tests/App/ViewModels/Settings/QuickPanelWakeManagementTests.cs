using System.Text.Json;
using Lertaro.App.ViewModels.Settings;
using Lertaro.Core;
using Lertaro.Core.Hook.InlineSearch;
using Lertaro.Core.Wire;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass, DoNotParallelize]
public sealed class QuickPanelWakeManagementTests
{
    [TestMethod]
    public void NewPresetsAreDisabledAndUndoRestoresRulesAndScopeWithoutTouchingSavedSettings()
    {
        var settings = new UserSettings(); var vm = new HotkeySettingsViewModel(settings, new(settings), (_, _) => false);
        try
        {
            var wake = vm.QuickPanelWake; var original = JsonSerializer.Serialize(wake.Export());
            for (var preset = 0; preset < 6; preset++)
            {
                wake.SelectedPreset = preset; wake.AddCommand.Execute(null);
                Assert.IsFalse(wake.Rows[^1].Enabled); Assert.IsTrue(wake.Rows[^1].IsEditing);
            }
            Assert.HasCount(9, wake.Rows); Assert.IsTrue(wake.IsDirty);
            wake.AllowDesktop = false;
            wake.MouseWhitelist.NewProcessName = "editor.exe"; wake.MouseWhitelist.AddProcessCommand.Execute(null);
            wake.Undo(); Assert.AreEqual(original, JsonSerializer.Serialize(wake.Export()));
            Assert.IsTrue(wake.AllowDesktop); Assert.IsEmpty(wake.MouseWhitelist.Items); Assert.IsFalse(wake.IsDirty);
            Assert.IsNull(settings.Hotkeys.QuickPanelWakeBindings); Assert.IsTrue(settings.Hotkeys.QuickPanelMouseAllowDesktop);
        }
        finally { vm.Cleanup(); }
    }
    [TestMethod]
    public void MultipleKeysSurviveSaveAndReopenAndDeletingAllDoesNotRestoreTheDefault()
    {
        var settings = new UserSettings(); var vm = new HotkeySettingsViewModel(settings, new(settings), (_, _) => false);
        try
        {
            for (var i = 3; i <= 8; i++)
            {
                vm.QuickPanelWake.SelectedPreset = 0; vm.QuickPanelWake.AddCommand.Execute(null);
                vm.QuickPanelWake.Rows[^1].Key = $"Ctrl+F{i}"; vm.QuickPanelWake.Rows[^1].Enabled = true;
            }
            Assert.IsTrue(vm.QuickPanelWake.Validate()); vm.Apply();
            Assert.HasCount(7, settings.Hotkeys.QuickPanelWakeBindings!.Where(b => b.Enabled));
            var reopened = new HotkeySettingsViewModel(settings, new(settings), (_, _) => false);
            try
            {
                Assert.HasCount(9, reopened.QuickPanelWake.Rows);
                foreach (var row in reopened.QuickPanelWake.Rows.ToArray()) reopened.QuickPanelWake.RemoveCommand.Execute(row);
                reopened.Apply(); Assert.IsEmpty(settings.Hotkeys.QuickPanelWakeBindings!);
                Assert.AreEqual("", settings.Hotkeys.QuickPanelHotkey);
            }
            finally { reopened.Cleanup(); }
        }
        finally { vm.Cleanup(); }
    }
    [TestMethod]
    public void DuplicateMouseOrKeyboardAndOtherFeatureKeysAreExplainedBeforeSaving()
    {
        var settings = new UserSettings(); var vm = new HotkeySettingsViewModel(settings, new(settings), (_, _) => false);
        try
        {
            var wake = vm.QuickPanelWake; wake.AddCommand.Execute(null);
            var added = wake.Rows[^1]; added.Key = "Ctrl+F2"; added.Enabled = true;
            Assert.IsFalse(wake.IsValid); added.Key = "Ctrl+F"; Assert.IsFalse(wake.IsValid);
            added.Key = "Ctrl+Alt+F8"; Assert.IsTrue(wake.IsValid);
            var hold = wake.Rows.Single(r => r.Kind == QuickPanelWakeKind.Hold); hold.Enabled = true;
            wake.SelectedPreset = 1; wake.AddCommand.Execute(null); wake.Rows[^1].Enabled = true;
            Assert.IsFalse(wake.IsValid); wake.Rows[^1].Enabled = false; Assert.IsTrue(wake.IsValid);
            Assert.IsNull(settings.Hotkeys.QuickPanelWakeBindings);
        }
        finally { vm.Cleanup(); }
    }
    [TestMethod]
    public void DoubleTapWakeConflictsWithAnUnmodifiedPluginActionKey()
    {
        var settings = new UserSettings();
        settings.Hotkeys.PluginActionHotkeys["test-plugin"] = new() { ["TestAction"] = "F2" };
        var vm = new HotkeySettingsViewModel(settings, new(settings), (_, _) => false);
        try
        {
            var row = vm.QuickPanelWake.Rows[0];
            row.Key = "DoubleTap:F2";
            row.Enabled = true;
            Assert.IsFalse(vm.QuickPanelWake.IsValid);
        }
        finally { vm.Cleanup(); }
    }
    [TestMethod]
    public void DoubleTapWakeConflictsWithAnEnabledDynamicPluginAction()
    {
        var settings = new UserSettings();
        settings.PluginSettings["Lertaro.Plugins.CustomActions"] = new()
        {
            ["Actions"] = JsonSerializer.SerializeToElement(new[]
            {
                new { Enabled = true, Hotkey = "DoubleTap:F2" }
            })
        };
        var vm = new HotkeySettingsViewModel(settings, new(settings), (_, _) => false);
        try
        {
            var row = vm.QuickPanelWake.Rows[0];
            row.Key = "DoubleTap:F2";
            row.Enabled = true;
            Assert.IsFalse(vm.QuickPanelWake.IsValid);
        }
        finally { vm.Cleanup(); }
    }
    [TestMethod]
    public void OperatingSystemConflictKeepsTheStagedKeyAndDoesNotOverwriteSavedRules()
    {
        var settings = new UserSettings(); var vm = new HotkeySettingsViewModel(settings, new(settings), (_, _) => true);
        try
        {
            vm.QuickPanelWake.Rows[0].Key = "Ctrl+Alt+F8";
            Assert.Throws<InvalidOperationException>(() => vm.Apply());
            Assert.AreEqual("Ctrl+Alt+F8", vm.QuickPanelWake.Rows[0].Key);
            Assert.AreEqual("Ctrl+F2", settings.Hotkeys.QuickPanelHotkey); Assert.IsNull(settings.Hotkeys.QuickPanelWakeBindings);
        }
        finally { vm.Cleanup(); }
    }
    [StaTestMethod]
    public async Task TestUsesAnUnsavedSnapshotAndEditingOrClosingRestoresNormalMode()
    {
        var settings = new UserSettings(); var parent = new HotkeySettingsViewModel(settings, new(settings), (_, _) => false);
        var sent = new List<IpcMessage>();
        var wake = new QuickPanelWakeSettingsViewModel(parent, settings, (_, _) => false, msg => { sent.Add(msg); return Task.FromResult(true); });
        try
        {
            var hold = wake.Rows.Single(r => r.Kind == QuickPanelWakeKind.Hold); hold.HoldMilliseconds = 900;
            await wake.StartTestAsync(hold);
            var request = JsonSerializer.Deserialize<QuickPanelWakeTestRequest>(sent[^1].StringVal1!)!;
            Assert.AreEqual(900, request.Binding.HoldMilliseconds); Assert.IsTrue(request.Binding.Enabled);
            Assert.AreNotEqual(hold.Id, request.Binding.Id); Assert.IsTrue(wake.IsTesting);
            Assert.AreEqual(600, settings.Hotkeys.QuickPanelRightHoldMilliseconds); Assert.IsFalse(settings.Hotkeys.QuickPanelMouseRightHold);
            wake.ReceiveTestResult("old-test", "Wake_TestSuccess"); Assert.DoesNotContain("Success", wake.TestMessage);
            hold.HoldMilliseconds = 1000;
            Assert.IsFalse(wake.IsTesting); Assert.AreEqual(IpcMessageId.StopQuickPanelWakeTest, sent[^1].Id);
            await wake.StartTestAsync(hold); wake.Dispose();
            Assert.AreEqual(IpcMessageId.StopQuickPanelWakeTest, sent[^1].Id); Assert.IsFalse(wake.IsTesting);
        }
        finally { wake.Dispose(); parent.Cleanup(); }
    }
    [StaTestMethod]
    public async Task AFailedTestConnectionDoesNotClaimToBeTestingOrModifySettings()
    {
        var settings = new UserSettings(); var parent = new HotkeySettingsViewModel(settings, new(settings), (_, _) => false);
        using var wake = new QuickPanelWakeSettingsViewModel(parent, settings, (_, _) => false, _ => Task.FromResult(false));
        try
        {
            await wake.StartTestAsync(wake.Rows[0]);
            Assert.IsFalse(wake.IsTesting); Assert.IsNotEmpty(wake.TestMessage); Assert.IsNull(settings.Hotkeys.QuickPanelWakeBindings);
        }
        finally { parent.Cleanup(); }
    }
}
