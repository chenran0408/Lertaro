using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services.AppWindow;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

public sealed class QuickLaunchSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _newName = string.Empty;
    private string _newPath = string.Empty;
    private bool _isEnabled;
    private bool _showShortcutBadges;

    public QuickLaunchSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        _isEnabled = userSettings.QuickLaunch.Enabled;
        _showShortcutBadges = userSettings.QuickLaunch.ShowShortcutBadges;
        SelectSectionCommand = new RelayCommand<string>(section => SelectedSection = section ?? "Items");
        foreach (var item in userSettings.QuickLaunch.Items)
            Items.Add(new QuickLaunchItemViewModel { Name = item.Name, Path = item.Path });
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasItems));
            CommandManager.InvalidateRequerySuggested();
        };
        _isAddFormOpen = Items.Count == 0;

        var disabledIds = userSettings.QuickLaunch.DisabledSourceIds;
        var sourceOptions = QuickLaunchSourceCatalog.Providers
            .Select(provider =>
            {
                var id = QuickLaunchSourceCatalog.GetId(provider);
                return new QuickLaunchSourceOptionViewModel(id, provider.Name,
                    !disabledIds.Contains(id, StringComparer.OrdinalIgnoreCase));
            })
            .ToList();
        var sourceOptionsById = sourceOptions.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in QuickLaunchSourceCatalog.OrderSourceIds(
                     sourceOptions.Select(source => source.Id), userSettings.QuickLaunch.SourceOrder))
        {
            Sources.Add(sourceOptionsById[id]);
        }

        AddCommand = new RelayCommand(Add, CanAdd);
        ClearCommand = new RelayCommand(Clear);
        RemoveCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Items.Remove(item));
        EditCommand = new RelayCommand<QuickLaunchItemViewModel>(Edit);
        SaveEditCommand = new RelayCommand<QuickLaunchItemViewModel>(SaveEdit, CanSaveEdit);
        CancelEditCommand = new RelayCommand<QuickLaunchItemViewModel>(CancelEdit);
        MoveUpCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Move(item, -1), item => CanMove(item, -1));
        MoveDownCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Move(item, 1), item => CanMove(item, 1));
        MoveSourceUpCommand = new RelayCommand<QuickLaunchSourceOptionViewModel>(source => MoveSource(source, -1),
            source => CanMoveSource(source, -1));
        MoveSourceDownCommand = new RelayCommand<QuickLaunchSourceOptionViewModel>(source => MoveSource(source, 1),
            source => CanMoveSource(source, 1));
        Sources.CollectionChanged += (_, _) => CommandManager.InvalidateRequerySuggested();
        BrowseFolderCommand = new RelayCommand(() => Browse(true));
        BrowseFileCommand = new RelayCommand(() => Browse(false));
        BrowseEditFolderCommand = new RelayCommand<QuickLaunchItemViewModel>(item => BrowseEdit(item, true));
        BrowseEditFileCommand = new RelayCommand<QuickLaunchItemViewModel>(item => BrowseEdit(item, false));
    }

    public ObservableCollection<QuickLaunchItemViewModel> Items { get; } = new();
    public ObservableCollection<QuickLaunchSourceOptionViewModel> Sources { get; } = new();
    public ICommand SelectSectionCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand MoveSourceUpCommand { get; }
    public ICommand MoveSourceDownCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand BrowseFileCommand { get; }
    public ICommand BrowseEditFolderCommand { get; }
    public ICommand BrowseEditFileCommand { get; }

    public bool HasItems => Items.Count > 0;
    private bool _isAddFormOpen;
    public bool IsAddFormOpen { get => _isAddFormOpen; set => SetProperty(ref _isAddFormOpen, value); }

    private string _filterText = string.Empty;
    public bool CanReorder => string.IsNullOrWhiteSpace(FilterText);
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetProperty(ref _filterText, value)) return;
            var query = value.Trim();
            CollectionViewSource.GetDefaultView(Items).Filter = query.Length == 0 ? null :
                row => row is QuickLaunchItemViewModel item && (item.IsEditing
                    || item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || item.Path.Contains(query, StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(CanReorder));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public bool ShowShortcutBadges
    {
        get => _showShortcutBadges;
        set => SetProperty(ref _showShortcutBadges, value);
    }

    private string _selectedSection = "Items";
    public string SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    public string NewName
    {
        get => _newName;
        set => SetProperty(ref _newName, value);
    }

    public string NewPath
    {
        get => _newPath;
        set
        {
            if (!SetProperty(ref _newPath, value)) return;
            CommandManager.InvalidateRequerySuggested();
            if (string.IsNullOrWhiteSpace(NewName) && !FavoriteUrlHelper.IsWebUrl(value))
                NewName = LaunchItemNameHelper.GetAutomaticName(value);
        }
    }

    public void NotifyLanguageChanged()
    {
        foreach (var source in Sources)
        {
            if (QuickLaunchSourceCatalog.Find(source.Id) is { } provider)
                source.Name = provider.Name;
        }
    }

    public void Save()
    {
        var settings = _userSettings.QuickLaunch;
        settings.Enabled = IsEnabled;
        settings.ShowShortcutBadges = ShowShortcutBadges;
        settings.Items = Items.Select(item => new QuickLaunchItemSetting { Name = item.Name, Path = item.Path }).ToList();
        var visibleIds = Sources.Select(source => source.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.DisabledSourceIds = settings.DisabledSourceIds
            .Where(id => !visibleIds.Contains(id))
            .Concat(Sources.Where(source => !source.IsEnabled).Select(source => source.Id))
            .ToList();
        var sourceIds = Sources.Select(source => source.Id).ToList();
        var currentSourceIds = sourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.SourceOrder = sourceIds
            .Concat(settings.SourceOrder.Where(id => !currentSourceIds.Contains(id)))
            .ToList();
    }

    private bool CanAdd()
    {
        var path = NormalizePath(NewPath);
        if (!FavoritePathResolver.IsPathAvailable(path)) return false;
        var normalized = FavoritePathResolver.NormalizeForComparison(path);
        return !Items.Any(item => string.Equals(FavoritePathResolver.NormalizeForComparison(item.Path),
            normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void Clear() => Items.Clear();

    private void Add()
    {
        if (!CanAdd()) return;
        Items.Add(new QuickLaunchItemViewModel { Name = NewName.Trim(), Path = NormalizePath(NewPath) });
        NewName = string.Empty;
        NewPath = string.Empty;
    }

    private void Edit(QuickLaunchItemViewModel? item)
    {
        if (item == null) return;
        foreach (var other in Items.Where(other => other != item))
            other.IsEditing = false;
        item.EditName = item.Name;
        item.EditPath = item.Path;
        item.IsEditing = true;
    }

    private bool CanSaveEdit(QuickLaunchItemViewModel? item)
    {
        if (item == null) return false;
        var path = NormalizePath(item.EditPath);
        return FavoritePathResolver.IsPathAvailable(path)
            && !Items.Any(other => other != item
                && string.Equals(FavoritePathResolver.NormalizeForComparison(other.Path),
                    FavoritePathResolver.NormalizeForComparison(path), StringComparison.OrdinalIgnoreCase));
    }

    private void SaveEdit(QuickLaunchItemViewModel item)
    {
        if (!CanSaveEdit(item)) return;
        item.Name = string.IsNullOrWhiteSpace(item.EditName) ? string.Empty : item.EditName.Trim();
        item.Path = NormalizePath(item.EditPath);
        item.IsEditing = false;
        RefreshFilter();
    }

    private void CancelEdit(QuickLaunchItemViewModel item)
    {
        if (item == null) return;
        item.IsEditing = false;
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        if (!CanReorder) CollectionViewSource.GetDefaultView(Items).Refresh();
    }

    private void BrowseEdit(QuickLaunchItemViewModel item, bool folder)
    {
        if (item == null) return;
        if (folder)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (AppWindowManager.ShowSettingsFileDialog(dialog) == true) item.EditPath = dialog.FolderName;
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { DereferenceLinks = false };
            if (AppWindowManager.ShowSettingsFileDialog(dialog) == true) item.EditPath = dialog.FileName;
        }
    }

    private static string NormalizePath(string value) => value.Trim().Trim('"');

    private bool CanMove(QuickLaunchItemViewModel? item, int offset)
    {
        if (!CanReorder || item == null) return false;
        var index = Items.IndexOf(item);
        var target = index + offset;
        return index >= 0 && target >= 0 && target < Items.Count;
    }

    private void Move(QuickLaunchItemViewModel? item, int offset)
    {
        if (item == null) return;
        var index = Items.IndexOf(item);
        var target = index + offset;
        if (index >= 0 && target >= 0 && target < Items.Count) Items.Move(index, target);
    }

    private bool CanMoveSource(QuickLaunchSourceOptionViewModel? source, int offset)
    {
        if (source == null) return false;
        var index = Sources.IndexOf(source);
        var target = index + offset;
        return index >= 0 && target >= 0 && target < Sources.Count;
    }

    private void MoveSource(QuickLaunchSourceOptionViewModel source, int offset)
    {
        if (!CanMoveSource(source, offset)) return;
        var index = Sources.IndexOf(source);
        Sources.Move(index, index + offset);
    }

    private void Browse(bool folder)
    {
        if (folder)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
        if (AppWindowManager.ShowSettingsFileDialog(dialog) == true) AddPaths(dialog.FolderNames);
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, DereferenceLinks = false };
        if (AppWindowManager.ShowSettingsFileDialog(dialog) == true) AddPaths(dialog.FileNames);
        }
    }

    internal void AddPaths(IEnumerable<string> paths)
    {
        var existing = Items
            .Select(item => FavoritePathResolver.NormalizeForComparison(item.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths)
        {
            var path = rawPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path) || !FavoritePathResolver.IsPathAvailable(path))
                continue;
            if (!existing.Add(FavoritePathResolver.NormalizeForComparison(path)))
                continue;

            Items.Add(new QuickLaunchItemViewModel
            {
                Name = LaunchItemNameHelper.GetAutomaticName(path),
                Path = path
            });
        }

        NewName = string.Empty;
        NewPath = string.Empty;
    }
}

public sealed class QuickLaunchItemViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    private string _editName = string.Empty;
    private string _editPath = string.Empty;
    private bool _isEditing;
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }
    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }
    public string EditPath
    {
        get => _editPath;
        set
        {
            if (SetProperty(ref _editPath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FavoritePathResolver.GetDisplayName(Path) : Name;
}

public sealed class QuickLaunchSourceOptionViewModel : ViewModelBase
{
    private string _name;
    private bool _isEnabled;
    public QuickLaunchSourceOptionViewModel(string id, string name, bool isEnabled)
    {
        Id = id;
        _name = name;
        _isEnabled = isEnabled;
    }
    public string Id { get; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
}
