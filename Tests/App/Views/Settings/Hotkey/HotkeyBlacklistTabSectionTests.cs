using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.Views.Settings.Hotkey;
using Lertaro.Core;
using Window = System.Windows.Window;
using Button = System.Windows.Controls.Button;
using TabControl = System.Windows.Controls.TabControl;
using ScrollViewer = System.Windows.Controls.ScrollViewer;

namespace Lertaro.App.Tests.Views.Settings.Hotkey;

[TestClass]
[DoNotParallelize]
public sealed class HotkeyBlacklistTabSectionTests
{
    [StaTestMethod]
    [DataRow(520.0)]
    [DataRow(780.0)]
    public void OnlyTheSelectedScopeIsVisibleAndEditingCanBeCancelled(double width)
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings { BlacklistedProcesses = new() { "editor.exe" } });
        vm.Global.SetRunningProcesses(new[] { new Lertaro.App.Services.RunningProcessInfo("editor.exe", "Document", true) });
        vm.Global.EditProcessCommand.Execute(vm.Global.Items[0]);
        var section = new HotkeyBlacklistTabSection { DataContext = new { Blacklist = vm } };
        var window = new Window { Content = new ScrollViewer { Content = section }, Width = width, Height = 700 };
        try
        {
            window.Show();
            Drain(window);
            var tabs = Find<TabControl>(section);
            Assert.IsNotNull(tabs);
            var global = Assert.IsInstanceOfType<ContentControl>(section.FindName("RowBlacklistGlobal"));
            var panel = Assert.IsInstanceOfType<ContentControl>(section.FindName("RowQuickPanelBlacklist"));
            Assert.IsTrue(global.IsVisible);
            Assert.IsFalse(panel.IsVisible);
            Assert.AreSame(vm.Global, global.Content);
            var buttons = new List<Button>();
            Collect(global, buttons);
            var confirm = buttons.Single(b => ReferenceEquals(b.Command, vm.Global.AddProcessCommand));
            Assert.IsTrue(confirm.IsVisible);
            var position = confirm.TranslatePoint(new Point(), section);
            Assert.IsLessThanOrEqualTo(section.ActualWidth + 0.1, position.X + confirm.ActualWidth);
            var cancel = buttons.Single(b => ReferenceEquals(b.Command, vm.Global.CancelEditCommand));
            Assert.IsTrue(cancel.IsVisible);
            cancel.Command.Execute(null);
            Drain(window);
            Assert.IsFalse(cancel.IsVisible);
            Assert.AreEqual("editor.exe", vm.Global.Items.Single().Value);
            tabs.SelectedIndex = 1;
            Drain(window);
            Assert.IsFalse(global.IsVisible);
            Assert.IsTrue(panel.IsVisible);
            Assert.AreSame(vm.QuickPanel, panel.Content);
            Assert.IsFalse(vm.QuickPanel.IsManualEditorOpen);
            Assert.IsEmpty(vm.QuickPanel.Items);
        }
        finally { window.Close(); }
    }

    [StaTestMethod]
    public void ProcessPickerAndTabsFollowThemeChangesWithoutNativeLightChrome()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings());
        vm.Global.SetRunningProcesses([new Lertaro.App.Services.RunningProcessInfo("editor.exe", "Document", true)]);
        var section = new HotkeyBlacklistTabSection { DataContext = new { Blacklist = vm } };
        var window = new Window { Content = section, Width = 780, Height = 700 };
        try
        {
            window.Show();
            Drain(window);
            var expander = Find<Expander>(section)!;
            System.Windows.Data.BindingOperations.ClearBinding(expander, Expander.IsExpandedProperty);
            expander.IsExpanded = true;
            foreach (var dark in new[] { true, false, true })
            {
                window.Resources.MergedDictionaries.Clear();
                window.Resources.MergedDictionaries.Add(new Lertaro.App.Services.Theme.BuiltInTheme(dark ? "Dark" : "Light", dark).GetResources());
                Drain(window);
                var list = Find<ListBox>(section)!;
                var expectedBackground = (SolidColorBrush)window.FindResource("ControlBackground");
                var expectedText = (SolidColorBrush)window.FindResource("TextPrimary");
                Assert.AreEqual(expectedBackground.Color, ((SolidColorBrush)list.Background).Color);
                Assert.AreEqual(expectedText.Color, ((SolidColorBrush)list.Foreground).Color);
                var check = Find<CheckBox>(list)!;
                Assert.IsNotNull(check.Template.FindName("CheckMark", check), "Per-row selection must retain the themed checkbox template.");
                var expanders = new List<Expander>();
                Collect(section, expanders);
                foreach (var sectionExpander in expanders)
                    Assert.AreEqual(expectedText.Color, ((SolidColorBrush)sectionExpander.Foreground).Color,
                        "Both running-process and manual/bulk headers must follow the theme.");
                var tabs = Find<TabControl>(section)!;
                Assert.IsNotNull(tabs.Template.FindName("PART_SelectedContentHost", tabs));
                Assert.AreEqual(Colors.Transparent, ((SolidColorBrush)tabs.Background).Color);
            }
        }
        finally { window.Close(); }
    }

    private static void Drain(Window window) => window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
    private static void Collect<T>(DependencyObject root, List<T> items) where T : DependencyObject
    {
        if (root is T match) items.Add(match);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Collect(VisualTreeHelper.GetChild(root, i), items);
    }
}
