using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.ViewModels.Settings.Plugins;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lertaro.App.Views.Settings.Plugins;

public partial class PluginManagementSettingsPage : System.Windows.Controls.UserControl
{
    public PluginManagementSettingsPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private PluginManagementViewModel? ViewModel => DataContext as PluginManagementViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PluginManagementViewModel oldVm)
        {
            oldVm.PluginsReordered -= OnPluginsReordered;
        }
        if (e.NewValue is PluginManagementViewModel newVm)
        {
            newVm.PluginsReordered += OnPluginsReordered;
        }
    }

    private void OnPluginsReordered() =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (PluginsList.SelectedItem is { } selection)
                PluginsList.ScrollIntoView(selection);
        }), DispatcherPriority.Background);
    private void PluginsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != PluginsList || PluginsList.SelectedItem is not PluginInfoViewModel plugin || ViewModel is not { } vm)
            return;
        vm.SelectedPlugin = plugin;
        PluginsList.ScrollIntoView(plugin);
    }

    private void PluginsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TreeWalk.Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.Content is PluginInfoViewModel)
            OpenSelectedPlugin();
    }

    private void PluginSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel is not { IsConfigurationDirectory: true } vm) return;
        e.Handled = true;
        if (vm.SelectedPlugin != null) OpenSelectedPlugin();
    }

    private void PluginsList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || PluginsList.SelectedItem == null
            || TreeWalk.Ancestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) != null) return;
        e.Handled = true;
        OpenSelectedPlugin();
    }

    private void OpenPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PluginInfoViewModel plugin } && ViewModel is { } vm)
        {
            if (plugin.HasConfigFields && Window.GetWindow(this) is SettingsWindow settings)
                settings.SelectSection("PluginSettings");
            if (vm.IsConfigurationDirectory && !vm.VisiblePlugins.Contains(plugin))
                vm.RevealPluginDetails(plugin);
            vm.SelectedPlugin = plugin;
            OpenSelectedPlugin();
        }
    }

    private void OpenSelectedPlugin()
    {
        if (ViewModel is not { SelectedPlugin: not null } vm) return;
        vm.SelectedPlugin.IsConfigTab = vm.IsConfigurationDirectory && vm.SelectedPlugin.HasConfigFields;
        vm.IsPluginDetailsOpen = true;
        if (BackToPluginsButton.IsVisible)
            BackToPluginsButton.Focus();
    }

    private void BackToPlugins_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        vm.IsPluginDetailsOpen = false;
        vm.RefreshInstalledFilter();
        if (PluginsList.SelectedItem is { } plugin)
        {
            PluginsList.ScrollIntoView(plugin);
            PluginsList.UpdateLayout();
            if (PluginsList.ItemContainerGenerator.ContainerFromItem(plugin) is ListBoxItem row)
            {
                row.Focus();
                return;
            }
        }
        PluginSearchBox.Focus();
    }

    private void SdkGuide_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.DevGuideUri is { } uri)
            Helpers.UrlLauncher.Open(uri);
    }

    private void PluginColumnHeader_Click(object sender, RoutedEventArgs e) => ViewModel?.TogglePluginSort();
}
