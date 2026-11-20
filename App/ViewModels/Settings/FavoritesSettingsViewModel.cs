using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services.AppWindow;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

public class FavoritesSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    public event Action<object>? InvalidEdit;
    private bool _hasCustomName;
    private string _newName = string.Empty;
    private string _newPath = string.Empty;

    public FavoritesSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        foreach (var fav in _userSettings.Favorites)
        {
            Items.Add(new FavoriteItemViewModel { Name = fav.Name, Path = fav.Path, Hotkey = fav.Hotkey });
        }
        foreach (var item in Items) item.PropertyChanged += OnItemPropertyChanged;
        Items.CollectionChanged += (_, e) =>
        {
            if (e.OldItems != null)
                foreach (FavoriteItemViewModel item in e.OldItems) item.PropertyChanged -= OnItemPropertyChanged;
            if (e.NewItems != null)
                foreach (FavoriteItemViewModel item in e.NewItems) item.PropertyChanged += OnItemPropertyChanged;
            OnPropertyChanged(nameof(HasItems));
            FavoriteHotkeySettingsSupport.RefreshHints(this);
            CommandManager.InvalidateRequerySuggested();
        };
        FavoriteHotkeySettingsSupport.RefreshHints(this);
        VisibleItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ClearLabel));
        _isAddFormOpen = Items.Count == 0;

        AddCommand = new RelayCommand(Add, CanAdd);
        ShowAllCommand = new RelayCommand(() => FilterText = string.Empty);
        ClearCommand = new RelayCommand(Clear, () => CanReorder ? HasItems : MatchingItems().Any());
        RemoveCommand = new RelayCommand<FavoriteItemViewModel>(Remove);
        EditCommand = new RelayCommand<FavoriteItemViewModel>(Edit);
        SaveEditCommand = new RelayCommand<FavoriteItemViewModel>(SaveEdit, CanSaveEdit);
        CancelEditCommand = new RelayCommand<FavoriteItemViewModel>(CancelEdit);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        BrowseFileCommand = new RelayCommand(BrowseFile);
        BrowseEditFolderCommand = new RelayCommand<FavoriteItemViewModel>(item => BrowseEdit(item, true));
        BrowseEditFileCommand = new RelayCommand<FavoriteItemViewModel>(item => BrowseEdit(item, false));
        MoveUpCommand = new RelayCommand<FavoriteItemViewModel>(MoveUp, item => CanReorder && item != null && Items.IndexOf(item) > 0);
        MoveDownCommand = new RelayCommand<FavoriteItemViewModel>(MoveDown, CanMoveDown);
        AddPathPresetCommand = new RelayCommand<string>(AddPathPreset);
    }

    public ObservableCollection<FavoriteItemViewModel> Items { get; } = new();

    /// <summary>
    /// The settings object this page edits, shared with <c>FavoriteHotkeySettingsSupport</c>: the row
    /// hotkey state has to be rebuilt from the same list <see cref="Save"/> just wrote, not from a
    /// reloaded copy, or a failure would be reported against whichever row happens to hold that index.
    /// </summary>
    public UserSettings Settings => _userSettings;

    public ICommand AddCommand { get; }
    public ICommand ShowAllCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand BrowseFileCommand { get; }
    public ICommand BrowseEditFolderCommand { get; }
    public ICommand BrowseEditFileCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand AddPathPresetCommand { get; }

    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(ClearLabel));

    internal string? CustomName => _hasCustomName ? NewName.Trim() : null;
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
                row => row is FavoriteItemViewModel item && (item.IsEditing || MatchesFilter(item, query));
            OnPropertyChanged(nameof(CanReorder));
            OnPropertyChanged(nameof(ClearLabel));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FavoriteItemViewModel.Hotkey))
            FavoriteHotkeySettingsSupport.RefreshHints(this);
    }

    public string NewName
    {
        get => _newName;
        set
        {
            _hasCustomName = !string.IsNullOrWhiteSpace(value);
            SetProperty(ref _newName, value);
        }
    }

    public string NewPath
    {
        get => _newPath;
        set
        {
            if (!SetProperty(ref _newPath, value)) return;
            if (!_hasCustomName)
            {
                var name = string.IsNullOrWhiteSpace(value) || FavoriteUrlHelper.IsWebUrl(value)
                    ? string.Empty
                    : FavoritePathResolver.GetDisplayName(NormalizePath(value));
                SetProperty(ref _newName, name, nameof(NewName));
            }
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool CanAdd()
    {
        var path = NormalizePath(NewPath);
        if (!FavoritePathResolver.IsPathAvailable(path)) return false;
        var normalized = FavoritePathResolver.NormalizeForComparison(path);
        return !Items.Any(item => string.Equals(FavoritePathResolver.NormalizeForComparison(item.Path),
            normalized, StringComparison.OrdinalIgnoreCase));
    }

    private System.ComponentModel.ICollectionView VisibleItems => CollectionViewSource.GetDefaultView(Items);
    private static bool MatchesFilter(FavoriteItemViewModel item, string query) =>
        item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || item.Path.Contains(query, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<FavoriteItemViewModel> MatchingItems()
    {
        var query = FilterText.Trim();
        return query.Length == 0 ? Items : Items.Where(item => MatchesFilter(item, query));
    }

    private int VisibleCount => CanReorder ? Items.Count : MatchingItems().Count();
    public string ClearLabel => string.Format(Services.TranslationManager.Instance[
        CanReorder ? "List_ClearAllCount" : "List_ClearMatchesCount"], VisibleCount);

    private void Clear()
    {
        foreach (var row in MatchingItems().ToArray())
            Items.Remove(row);
    }

    private void Add()
    {
        if (!CanAdd()) return;
        var name = string.IsNullOrWhiteSpace(NewName) ? string.Empty : NewName.Trim();
        var path = NormalizePath(NewPath);

        if (string.IsNullOrWhiteSpace(path)) return;

        Items.Add(new FavoriteItemViewModel { Name = name, Path = path });
        NewName = string.Empty;
        NewPath = string.Empty;
    }

    public void AddPathPreset(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        NewPath = path;
        IsAddFormOpen = true;
    }

    private void Remove(FavoriteItemViewModel item)
    {
        if (item != null)
        {
            Items.Remove(item);
        }
    }

    private void Edit(FavoriteItemViewModel item)
    {
        if (item == null || item.IsEditing || !TryFinishEdit()) return;
        item.HasPathError = false;
        item.EditName = item.Name;
        item.EditPath = item.Path;
        item.IsEditing = true;
    }

    internal bool TryFinishEdit()
    {
        var row = Items.FirstOrDefault(item => item.IsEditing);
        if (row == null) return true;
        if (row.EditName == row.Name && row.EditPath == row.Path)
        {
            CancelEdit(row);
            return true;
        }
        if (CanSaveEdit(row))
        {
            SaveEdit(row);
            return true;
        }
        row.HasPathError = true;
        InvalidEdit?.Invoke(row);
        return false;
    }

    private bool CanSaveEdit(FavoriteItemViewModel? item)
    {
        if (item == null) return false;
        var path = NormalizePath(item.EditPath);
        return FavoritePathResolver.IsPathAvailable(path)
            && !Items.Any(other => other != item
                && string.Equals(FavoritePathResolver.NormalizeForComparison(other.Path),
                    FavoritePathResolver.NormalizeForComparison(path), StringComparison.OrdinalIgnoreCase));
    }

    private void SaveEdit(FavoriteItemViewModel item)
    {
        if (!CanSaveEdit(item)) return;
        item.Name = string.IsNullOrWhiteSpace(item.EditName) ? string.Empty : item.EditName.Trim();
        item.Path = NormalizePath(item.EditPath);
        item.IsEditing = false;
        RefreshFilter();
    }

    private void CancelEdit(FavoriteItemViewModel item)
    {
        if (item == null) return;
        item.IsEditing = false;
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        if (!CanReorder) CollectionViewSource.GetDefaultView(Items).Refresh();
    }

    private void BrowseEdit(FavoriteItemViewModel item, bool folder)
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

    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
        if (AppWindowManager.ShowSettingsFileDialog(dialog) == true) AddPaths(dialog.FolderNames);
    }

    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, DereferenceLinks = false };
        if (AppWindowManager.ShowSettingsFileDialog(dialog) == true) AddPaths(dialog.FileNames);
    }

    internal void AddPaths(IEnumerable<string> paths) => FavoritesSettingsPathSupport.AddPaths(this, paths);

    private void MoveUp(FavoriteItemViewModel item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0)
        {
            Items.Move(idx, idx - 1);
        }
    }

    private void MoveDown(FavoriteItemViewModel item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1)
        {
            Items.Move(idx, idx + 1);
        }
    }

    private bool CanMoveDown(FavoriteItemViewModel item)
    {
        if (!CanReorder || item == null) return false;
        var index = Items.IndexOf(item);
        return index >= 0 && index < Items.Count - 1;
    }

    public void Save()
    {
        if (!TryFinishEdit()) return;
        _userSettings.Favorites = Items.Select(x => new FavoriteItemSetting { Name = x.Name, Path = x.Path, Hotkey = x.Hotkey }).ToList();
    }

    /// <summary>
    /// Re-applies the global-hotkey registrations after the favorites above were written to settings, and
    /// surfaces per-row feedback: a combination another application already owns, or one a later favorite
    /// also claimed. Split out of <see cref="Save"/> because the registrations belong to the hotkey
    /// service while the row state belongs here -- <see cref="SettingsViewModel.Apply"/> is what calls
    /// this once the settings file itself has been written.
    /// </summary>
    public void ApplyHotkeys() => FavoriteHotkeySettingsSupport.ApplyHotkeys(this);
}
