using System.Windows.Data;
using Lertaro.App.Services;

namespace Lertaro.App.ViewModels.Settings.Plugins;

public partial class PluginManagementViewModel
{
    private string _installedPluginSearchText = string.Empty;
    public string InstalledPluginSearchText
    {
        get => _installedPluginSearchText;
        set
        {
            if (SetProperty(ref _installedPluginSearchText, value)) RefreshInstalledFilter();
        }
    }

    private string _installedPluginFilter = "All";
    public string InstalledPluginFilter
    {
        get => _installedPluginFilter;
        set
        {
            if (SetProperty(ref _installedPluginFilter, value)) RefreshInstalledFilter();
        }
    }

    public string PluginSearchLabel => TranslationManager.Instance[IsConfigurationDirectory ? "Plugins_SearchSettings" : "Plugins_SearchInstalled"];

    public IEnumerable<PluginConfigurationCategory> CategoryChoices => new[] { new PluginConfigurationCategory("All") }
        .Concat(Plugins.Where(p => p.HasConfigFields).Select(p => p.ConfigurationCategoryKey)
            .Distinct().Select(key => new PluginConfigurationCategory(key)).OrderBy(option => option.Name));

    private string _selectedCategory = "All";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value != null && SetProperty(ref _selectedCategory, value)) RefreshInstalledFilter();
        }
    }

    private bool _isConfigurationDirectory;
    private string _otherDirectorySearch = string.Empty;
    public bool IsConfigurationDirectory
    {
        get => _isConfigurationDirectory;
        set
        {
            if (!SetProperty(ref _isConfigurationDirectory, value)) return;
            (_installedPluginSearchText, _otherDirectorySearch) = (_otherDirectorySearch, _installedPluginSearchText);
            OnPropertyChanged(nameof(InstalledPluginSearchText));
            OnPropertyChanged(nameof(PluginSearchLabel));
            OnPropertyChanged(nameof(CategoryChoices));
            IsPluginDetailsOpen = false;
            IsRuntimeStatusTab = false;
            using (VisiblePlugins.DeferRefresh())
            {
                VisiblePlugins.GroupDescriptions.Clear();
                if (value)
                {
                    var group = new PropertyGroupDescription(nameof(PluginInfoViewModel.ConfigurationCategory));
                    group.SortDescriptions.Add(new(nameof(CollectionViewGroup.Name), System.ComponentModel.ListSortDirection.Ascending));
                    VisiblePlugins.GroupDescriptions.Add(group);
                }
            }
            SelectVisiblePlugin();
        }
    }

    private bool _isPluginDetailsOpen;
    public bool IsPluginDetailsOpen
    {
        get => _isPluginDetailsOpen;
        set => SetProperty(ref _isPluginDetailsOpen, value);
    }

    internal void RevealPluginDetails(PluginInfoViewModel plugin)
    {
        IsRuntimeStatusTab = false;
        InstalledPluginSearchText = string.Empty;
        if (!IsConfigurationDirectory) InstalledPluginFilter = "All";
        else if (SelectedCategory != plugin.ConfigurationCategoryKey) SelectedCategory = "All";
        SelectedPlugin = plugin;
        IsPluginDetailsOpen = true;
    }

    private bool MatchesInstalledFilter(object item)
    {
        var plugin = (PluginInfoViewModel)item;
        if (IsConfigurationDirectory && (!plugin.HasConfigFields
            || (SelectedCategory != "All" && SelectedCategory != plugin.ConfigurationCategoryKey))) return false;
        if (!IsConfigurationDirectory && (InstalledPluginFilter == "Enabled" && plugin.IsFullyDisabled
            || InstalledPluginFilter == "Disabled" && !plugin.IsFullyDisabled
            || InstalledPluginFilter == "Configurable" && !plugin.HasConfigFields)) return false;
        var query = InstalledPluginSearchText.Trim();
        return (IsConfigurationDirectory && plugin.ConfigurationCategory.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            || plugin.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || plugin.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || plugin.DllFileName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    internal void RefreshInstalledFilter()
    {
        VisiblePlugins.Refresh();
        SelectVisiblePlugin();
    }

    private void SelectVisiblePlugin()
    {
        if (SelectedPlugin == null || !VisiblePlugins.Contains(SelectedPlugin))
            SelectedPlugin = VisiblePlugins.Cast<PluginInfoViewModel>().FirstOrDefault();
    }
}
