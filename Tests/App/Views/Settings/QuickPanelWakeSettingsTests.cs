using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Services.Theme;
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
    public void TwoKeysAndMouseAllowlistAreStagedAndSaveTogether(string theme, bool dark)
    {
        var settings = new UserSettings();
        var blacklist = new BlacklistSettingsViewModel(settings);
        var vm = new HotkeySettingsViewModel(settings, blacklist, (_, _) => false);
        var page = new HotkeyQuickPanelWakeSection { DataContext = vm };
        page.Resources.MergedDictionaries.Add(new BuiltInTheme(theme, dark).GetResources());
        var window = new Window { Content = page, Width = 750, Height = 650, ShowActivated = false };
        try
        {
            window.Show(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreSame(page.FindResource("TextPrimary"), page.Foreground);
            var hold = (CheckBox)page.FindName("RowMouseRightHold");
            var desktop = (CheckBox)page.FindName("RowMouseAllowDesktop");
            Assert.AreEqual(false, hold.IsChecked); Assert.AreEqual(true, desktop.IsChecked);
            hold.IsChecked = true; desktop.IsChecked = false;
            vm.QuickPanelAlternateHotkey = "Ctrl+Alt+F8";
            vm.QuickPanelMouseCircle = true; vm.QuickPanelRightHoldMilliseconds = 800;
            vm.MouseWakeWhitelist.NewProcessName = "editor.exe"; vm.MouseWakeWhitelist.AddProcessCommand.Execute(null);
            Assert.IsFalse(settings.Hotkeys.QuickPanelMouseRightHold);
            Assert.IsEmpty(settings.Hotkeys.QuickPanelMouseAllowedProcesses);
            vm.Apply();
            Assert.AreEqual("Ctrl+F2", settings.Hotkeys.QuickPanelHotkey);
            Assert.AreEqual("Ctrl+Alt+F8", settings.Hotkeys.QuickPanelAlternateHotkey);
            Assert.IsTrue(settings.Hotkeys.QuickPanelMouseRightHold);
            Assert.IsTrue(settings.Hotkeys.QuickPanelMouseCircle);
            Assert.IsFalse(settings.Hotkeys.QuickPanelMouseAllowDesktop);
            Assert.AreEqual(800, settings.Hotkeys.QuickPanelRightHoldMilliseconds);
            Assert.HasCount(1, settings.Hotkeys.QuickPanelMouseAllowedProcesses);
            Assert.IsEmpty(settings.BlacklistedProcesses); Assert.IsEmpty(settings.QuickPanel.BlacklistedProcesses);
        }
        finally { window.Close(); vm.Cleanup(); }
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
