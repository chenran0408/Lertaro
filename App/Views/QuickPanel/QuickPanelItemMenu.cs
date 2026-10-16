using System.Windows.Controls.Primitives;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.App.Services;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Helpers;
using Lertaro.App.Services.ShellMenu.ActionFlyout;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ListBox = System.Windows.Controls.ListBox;

namespace Lertaro.App.Views.QuickPanel;

// UI orchestration for panel-only item names and adding shortcuts to an explicitly selected cell.
internal sealed class QuickPanelItemMenu
{
    private readonly QuickPanelWindow window;
    internal QuickPanelItemMenu(QuickPanelWindow window)
    {
        this.window = window;
        window.PreviewMouseDown += (_, _) => { if (_building) Close(); };
        window.Deactivated += (_, _) => { if (_building) Close(); };
        window.IsVisibleChanged += (_, _) => { if (!window.IsVisible) Close(); };
        window.Closed += (_, _) => Close();
    }
    private ContextMenu? _menu;
    private bool _building;
    private int _generation;
    internal bool IsOpen => _building || _menu?.IsOpen == true;
    internal void Close()
    {
        _generation++;
        _building = false;
        _menu?.IsOpen = false;
        _menu?.Items.Clear();
    }
    internal async Task ShowAsync(ListBox list, QuickPanelGroupViewModel group, AppSearchResult? item, int slot)
    {
        Close();
        var generation = _generation;
        var menu = _menu ??= new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.PlacementTarget = list;
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

        }
        list.ContextMenu = menu;
        if (item != null)
        {
            var selection = list.SelectedItems.OfType<AppSearchResult>().ToList();
            if (!selection.Contains(item)) selection = new() { item };
            _building = true;
            try
            {
                using var existence = PathExistenceCache.BeginScope();
                await Task.Run(() => PathExistenceCache.PrimeResults(selection));
                if (_generation != generation || !window.IsVisible) return;
                PrependActions(menu, ActionMenuBuilder.FinalizeItems(ActionMenuBuilder.BuildStatic(selection, SearchWindowType.Main)), selection, window);
            }
            catch (Exception ex) { Core.Logger.Log($"[QuickPanel] Building item actions failed: {ex.Message}", Core.LogLevel.Error); }
            finally { if (_generation == generation) _building = false; }
        }
        if (_generation == generation && window.IsVisible) menu.IsOpen = true;
        MenuItem Add(string key, Action action)
        {
            var entry = new MenuItem { Header = TranslationManager.Instance[key] };
            entry.Click += (_, _) => { menu.IsOpen = false; action(); };
            menu.Items.Add(entry); return entry;
        }
    }
    // Keep built-in command routing and multi-selection identical to the shared action menu.
    internal static void PrependActions(ContextMenu menu, IReadOnlyList<ActionMenuItem> actions, IReadOnlyList<AppSearchResult> selection, IPluginSearchWindow view)
    {
        var index = 0;
        var commands = new Dictionary<uint, PluginSdk.Abstractions.Plugins.IDynamicActionProvider>();
        foreach (var action in actions)
        {
            if (action.IsSeparator) { menu.Items.Insert(index++, new System.Windows.Controls.Separator()); continue; }
            var entry = new MenuItem
            {
                Header = action.IsSectionHeader ? action.SectionTitle : action.Text,
                InputGestureText = action.ShortcutHint,
                IsEnabled = !action.IsSectionHeader && !action.IsDisabled,
                DataContext = action
            };
            if (action.Icon != null) entry.Icon = new System.Windows.Controls.Image { Source = action.Icon, Width = 16, Height = 16 };
            if (!action.IsSectionHeader)
                entry.Click += (_, _) =>
                {
                    menu.IsOpen = false;
                    menu.Dispatcher.BeginInvoke(new Action(() => ActionFlyoutItems.DispatchExecute(action, selection, commands, view)), System.Windows.Threading.DispatcherPriority.Background);
                };
            menu.Items.Insert(index++, entry);
        }
        if (index > 0 && index < menu.Items.Count) menu.Items.Insert(index, new System.Windows.Controls.Separator());
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
