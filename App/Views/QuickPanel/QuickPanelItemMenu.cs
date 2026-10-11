using System.Windows.Controls.Primitives;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.App.Services;
using Lertaro.PluginSdk.Abstractions;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ListBox = System.Windows.Controls.ListBox;

namespace Lertaro.App.Views.QuickPanel;

// UI orchestration for panel-only item names and adding shortcuts to an explicitly selected cell.
internal sealed class QuickPanelItemMenu(QuickPanelWindow window)
{
    private ContextMenu? _menu;
    internal bool IsOpen => _menu?.IsOpen == true;
    internal void Show(ListBox list, QuickPanelGroupViewModel group, AppSearchResult? item, int slot)
    {
        var menu = new ContextMenu { PlacementTarget = list, Placement = PlacementMode.MousePoint };
        if (item == null)
        {
            if (slot < 0 || !QuickPanelWindow.CanDrop(group, true, false) || !group.CanArrange) return;
            Add("QuickPanel_AddShortcut", () => AddShortcuts(group, slot, false));
            Add("QuickPanel_AddFolderShortcut", () => AddShortcuts(group, slot, true));
        }
        else
        {
            Add("QuickPanel_IconName", () => Rename(group, item));
            var hidden = group.ItemLabels.Values.TryGetValue(item.FullPath, out var label) && label.HideName;
            var toggle = Add("QuickPanel_HideIconName", () => group.ItemLabels.Set(item.FullPath, label?.DisplayName ?? "", !hidden));
            toggle.IsCheckable = true; toggle.IsChecked = hidden;
            Add("QuickPanel_ResetIconName", () => group.ItemLabels.Set(item.FullPath, "", false));
            menu.Items.Add(new System.Windows.Controls.Separator());
            Add("QuickPanel_MoreActions", () => Services.ShellMenu.ActionFlyout.ActionFlyout.Show(list.SelectedItems.OfType<AppSearchResult>().ToList(), window, window, list, PlacementMode.MousePoint, includeShellActions: false));
        }
        _menu = menu;
        menu.IsOpen = true;
        MenuItem Add(string key, Action action)
        {
            var entry = new MenuItem { Header = TranslationManager.Instance[key] };
            entry.Click += (_, _) => { menu.IsOpen = false; action(); };
            menu.Items.Add(entry); return entry;
        }
    }
    private void Rename(QuickPanelGroupViewModel group, AppSearchResult item) => WithPin(() =>
                                                                                      {
                                                                                          group.ItemLabels.Values.TryGetValue(item.FullPath, out var label);
                                                                                          var current = group.ItemLabels.Name(item.FullPath, item.Name, item.IsDir);
                                                                                          var values = Controls.Dialogs.PluginFieldPromptWindow.ShowPrompt(TranslationManager.Instance["QuickPanel_IconName"],
                                                                                              new[] { new PluginConfigField { Key = "Name", LabelKey = "QuickLaunch_FieldName", FieldType = ConfigFieldType.Text, DefaultValue = current } },
                                                                                              new Dictionary<string, object?> { ["Name"] = current });
                                                                                          if (values != null) group.ItemLabels.Set(item.FullPath, values.GetValueOrDefault("Name")?.ToString() ?? "", label?.HideName == true);
                                                                                      });
    private void AddShortcuts(QuickPanelGroupViewModel group, int slot, bool folder)
    {
        string[]? paths = null;
        WithPin(() =>
        {
            if (folder)
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
                if (dialog.ShowDialog(window) == true) paths = dialog.FolderNames;
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, CheckFileExists = true };
                if (dialog.ShowDialog(window) == true) paths = dialog.FileNames;
            }
        });
        if (paths == null) return;
        ShellThread.Run("Add panel shortcuts", () =>
        {
            try
            {
                var added = Services.QuickPanel.QuickPanelShortcutDrop.Add(paths, group.FolderPath);
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    group.PlaceInGrid(added, slot);
                    if (window.DataContext is QuickPanelViewModel vm) _ = vm.ReloadGroupAsync(group);
                }));
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[QuickPanel] Adding shortcuts failed: {ex.Message}", Core.LogLevel.Error);
                PluginSdk.Services.PluginNotificationService.Show(TranslationManager.Instance["QuickPanel_DropFailed"], ex.Message);
            }
        });
    }
    private void WithPin(Action action)
    {
        var pinned = window.IsStayOpen;
        window.IsStayOpen = true;
        try { action(); }
        finally { window.IsStayOpen = pinned; window.Activate(); }
    }
}
