using System.Collections.ObjectModel;
using System.Windows.Input;

using Lertaro.App.Helpers;
using Lertaro.App.Services;
namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// One editable list of process names: type-and-add, a row per entry, and the bulk text box for
/// pasting or exporting the lot. Extracted from <see cref="BlacklistSettingsViewModel"/> when the quick
/// panel's own list appeared beside the global one -- there is nothing about any of this that is
/// specific to which list is being edited, and the second one had no business being a bare text box
/// just because building the same thing twice was tedious.
/// </summary>
public class ProcessBlacklistEditorViewModel : ViewModelBase
{
    public ProcessBlacklistEditorViewModel(IEnumerable<string> initial)
    {
        foreach (var value in initial.Where(x => !string.IsNullOrWhiteSpace(x)))
            Items.Add(new BlacklistProcessItem(value));
        RefreshBulkText();

        AddProcessCommand = new RelayCommand(AddProcess, () => !string.IsNullOrWhiteSpace(NewProcessName));
        ApplyTextCommand = new RelayCommand(ApplyBulkText);
        ExportTextCommand = new RelayCommand(RefreshBulkText);
        RemoveProcessCommand = new RelayCommand<BlacklistProcessItem>(RemoveProcess);
        EditProcessCommand = new RelayCommand<BlacklistProcessItem>(EditProcess);
        RefreshProcessesCommand = new RelayCommand(() => _ = RefreshProcessesAsync(), () => !IsLoadingProcesses);
        AddSelectedProcessesCommand = new RelayCommand(AddSelectedProcesses, () => SelectedRunningCount > 0);
    }

    public ObservableCollection<BlacklistProcessItem> Items { get; } = new();

    public ICommand AddProcessCommand { get; }
    public ICommand ApplyTextCommand { get; }
    public ICommand ExportTextCommand { get; }
    public ICommand RemoveProcessCommand { get; }
    public ICommand EditProcessCommand { get; }
    public ICommand RefreshProcessesCommand { get; }
    public ICommand AddSelectedProcessesCommand { get; }
    public ObservableCollection<RunningProcessChoice> RunningProcesses { get; } = new();
    private readonly List<RunningProcessChoice> _runningChoices = new();
    public int SelectedRunningCount => _runningChoices.Count(x => x.IsSelected);

    private bool _isProcessPickerOpen;
    public bool IsProcessPickerOpen
    {
        get => _isProcessPickerOpen;
        set
        {
            if (SetProperty(ref _isProcessPickerOpen, value) && value) _ = RefreshProcessesAsync();
        }
    }

    private string _processSearch = string.Empty;
    public string ProcessSearch
    {
        get => _processSearch;
        set { if (SetProperty(ref _processSearch, value)) FilterProcesses(); }
    }

    private bool _showAllProcesses;
    public bool ShowAllProcesses
    {
        get => _showAllProcesses;
        set { if (SetProperty(ref _showAllProcesses, value)) FilterProcesses(); }
    }

    private bool _isLoadingProcesses;
    public bool IsLoadingProcesses
    {
        get => _isLoadingProcesses;
        private set { if (SetProperty(ref _isLoadingProcesses, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    private string _processLoadError = string.Empty;
    public string ProcessLoadError
    {
        get => _processLoadError;
        private set => SetProperty(ref _processLoadError, value);
    }

    private async Task RefreshProcessesAsync()
    {
        if (IsLoadingProcesses) return;
        IsLoadingProcesses = true;
        ProcessLoadError = string.Empty;
        try { SetRunningProcesses(await Task.Run(RunningProcessCatalog.Capture)); }
        catch (Exception ex)
        {
            Core.Logger.Log($"[ProcessBlacklist] Process enumeration failed: {ex.Message}", Core.LogLevel.Warn);
            ProcessLoadError = TranslationManager.Instance["Blacklist_ProcessLoadFailed"];
        }
        finally { IsLoadingProcesses = false; }
    }

    internal void SetRunningProcesses(IEnumerable<RunningProcessInfo> processes)
    {
        var selected = _runningChoices.Where(x => x.IsSelected).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _runningChoices.Clear();
        foreach (var process in RunningProcessCatalog.Normalize(processes))
        {
            var choice = new RunningProcessChoice(process) { IsSelected = selected.Contains(process.Name) };
            choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedRunningCount));
            _runningChoices.Add(choice);
        }
        FilterProcesses();
        OnPropertyChanged(nameof(SelectedRunningCount));
        CommandManager.InvalidateRequerySuggested();
    }

    private void FilterProcesses()
    {
        RunningProcesses.Clear();
        foreach (var choice in _runningChoices.Where(x => RunningProcessCatalog.Matches(x.Process, ProcessSearch, ShowAllProcesses)))
            RunningProcesses.Add(choice);
    }

    private void AddSelectedProcesses()
    {
        // Merge pending bulk edits first: adding from the picker must not silently discard them.
        ApplyBulkText();
        foreach (var choice in _runningChoices.Where(x => x.IsSelected))
        {
            AddUnique(choice.Name);
            choice.IsSelected = false;
        }
        RefreshBulkText();
    }

    private string _newProcessName = string.Empty;
    public string NewProcessName
    {
        get => _newProcessName;
        set
        {
            if (SetProperty(ref _newProcessName, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private string _bulkText = string.Empty;
    public string BulkText
    {
        get => _bulkText;
        set => SetProperty(ref _bulkText, value);
    }

    /// <summary>The list as it should be stored: trimmed, blanks and repeats dropped.</summary>
    public List<string> ToSettingsList()
    {
        ApplyBulkText();
        return Items.Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddProcess()
    {
        AddUnique(NewProcessName);
        NewProcessName = string.Empty;
        RefreshBulkText();
    }

    private void AddUnique(string value)
    {
        var normalized = value.Trim().Trim('"');
        if (normalized.Length == 0 || Core.ProcessNameFilter.Matches(normalized, Items.Select(x => x.Value)))
            return;
        Items.Add(new BlacklistProcessItem(normalized));
    }

    private void RemoveProcess(BlacklistProcessItem? item)
    {
        if (item == null)
            return;
        Items.Remove(item);
        RefreshBulkText();
    }

    // Edit is remove-and-refill: the row has no editor of its own, so the entry goes back into the box
    // it was typed in.
    private void EditProcess(BlacklistProcessItem? item)
    {
        if (item == null)
            return;
        NewProcessName = item.Value;
        Items.Remove(item);
        RefreshBulkText();
    }

    private void ApplyBulkText()
    {
        Items.Clear();
        foreach (var line in (BulkText ?? string.Empty).Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            AddUnique(line);
        RefreshBulkText();
    }

    private void RefreshBulkText() => BulkText = string.Join(Environment.NewLine, Items.Select(x => x.Value));
}
