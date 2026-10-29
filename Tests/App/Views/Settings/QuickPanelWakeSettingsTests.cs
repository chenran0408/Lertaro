using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.Views.Settings.Hotkey;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass, DoNotParallelize]
public sealed class QuickPanelWakeSettingsTests
{
    [StaTestMethod]
    [DataRow("Light", false)]
    [DataRow("Dark", true)]
    public void MigratedRulesAndSharedMouseScopeAreStagedAndSaveTogether(string theme, bool dark)
    {
        using var themeFixture = SettingsThemeFixture.Use(theme, dark);
        var settings = new UserSettings();
        var blacklist = new BlacklistSettingsViewModel(settings);
        var vm = new HotkeySettingsViewModel(settings, blacklist, (_, _) => false);
        var page = new HotkeyQuickPanelWakeSection { DataContext = vm };
        var window = new Window { Content = new ScrollViewer { Content = page }, Width = 850, Height = 700, ShowActivated = false };
        try
        {
            window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var testButtons = FindButtons(page).Where(b => ReferenceEquals(b.Command, vm.QuickPanelWake.TestCommand)).ToList();
            Assert.HasCount(3, testButtons, "Every rule must have a working Test command binding.");
            Assert.IsTrue(testButtons[0].Command!.CanExecute(testButtons[0].CommandParameter));
            testButtons[0].Command!.Execute(testButtons[0].CommandParameter);
            Assert.IsNotEmpty(vm.QuickPanelWake.TestMessage, "Testing must report success or a connection problem, never silently do nothing.");
            var desktop = (CheckBox)page.FindName("RowMouseAllowDesktop");
            var hold = vm.QuickPanelWake.Rows.Single(r => r.Kind == QuickPanelWakeKind.Hold);
            Assert.IsFalse(hold.Enabled); Assert.AreEqual(true, desktop.IsChecked);
            hold.Enabled = true; hold.HoldMilliseconds = 800; desktop.IsChecked = false;
            vm.QuickPanelWake.SelectedPreset = 0; vm.QuickPanelWake.AddCommand.Execute(null);
            var key = vm.QuickPanelWake.Rows[^1]; key.Key = "Ctrl+Alt+F8"; key.Enabled = true;
            vm.QuickPanelWake.Rows.Single(r => r.Kind == QuickPanelWakeKind.Circle).Enabled = true;
            vm.MouseWakeWhitelist.NewProcessName = "editor.exe"; vm.MouseWakeWhitelist.AddProcessCommand.Execute(null);
            Assert.IsFalse(settings.Hotkeys.QuickPanelMouseRightHold);
            Assert.IsEmpty(settings.Hotkeys.QuickPanelMouseAllowedProcesses);
            Assert.IsNull(settings.Hotkeys.QuickPanelWakeBindings);
            vm.Apply();
            Assert.AreEqual("Ctrl+F2", settings.Hotkeys.QuickPanelHotkey);
            Assert.AreEqual("Ctrl+Alt+F8", settings.Hotkeys.QuickPanelAlternateHotkey);
            Assert.IsTrue(settings.Hotkeys.QuickPanelMouseRightHold);
            Assert.IsTrue(settings.Hotkeys.QuickPanelMouseCircle);
            Assert.IsFalse(settings.Hotkeys.QuickPanelMouseAllowDesktop);
            Assert.AreEqual(800, settings.Hotkeys.QuickPanelRightHoldMilliseconds);
            Assert.HasCount(1, settings.Hotkeys.QuickPanelMouseAllowedProcesses);
            Assert.HasCount(4, settings.Hotkeys.QuickPanelWakeBindings!);
            Assert.IsEmpty(settings.BlacklistedProcesses); Assert.IsEmpty(settings.QuickPanel.BlacklistedProcesses);
        }
        finally { window.Close(); vm.Cleanup(); }
    }
    private static IEnumerable<Button> FindButtons(System.Windows.DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
    [TestMethod]
    public void AlternateShortcutUsesConflictProbeAndMouseWhitelistDoesNotAffectKeys()
    {
        var settings = new UserSettings();
        var vm = new HotkeySettingsViewModel(settings, new BlacklistSettingsViewModel(settings), (_, _) => true);
        try
        {
            vm.ToggleHotkeyValue = "Ctrl"; vm.QuickPanelHotkey = ""; vm.QuickSwitchComboHotkey = ""; vm.QuickNavigationHotkey = "";
            vm.QuickPanelAlternateHotkey = "Alt+Space";
            var taken = vm.ProbeGlobalCombinations();
            Assert.HasCount(1, taken); Assert.AreEqual("", vm.QuickPanelAlternateHotkey);
            Assert.IsTrue(vm.QuickPanelMouseAllowDesktop); Assert.IsFalse(vm.QuickPanelMouseCircle);
        }
        finally { vm.Cleanup(); }
    }
}
