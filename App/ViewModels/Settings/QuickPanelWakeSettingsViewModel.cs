using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.Favorites;
using Lertaro.Core;
using Lertaro.Core.Hook.InlineSearch;
using Lertaro.Core.Hook.Ipc;
using Lertaro.Core.Wire;

namespace Lertaro.App.ViewModels.Settings;

public sealed class QuickPanelWakeSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly HotkeySettingsViewModel _parent;
    private readonly UserSettings _settings;
    private readonly Func<uint, uint, bool> _combinationTaken;
    private readonly HookIpcClient? _client;
    private readonly Func<IpcMessage, Task<bool>> _send;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _testTimer;
    private List<QuickPanelWakeBinding> _baseline;
    private bool _baselineDesktop;
    private List<string> _baselineProcesses;
    private bool _restoring, _disposed;
    private string _error = "", _testMessage = "", _lastTestResult = "";
    private string? _testId;
    private int _remaining;
    private bool _isTesting;

    public QuickPanelWakeSettingsViewModel(HotkeySettingsViewModel parent, UserSettings settings,
        Func<uint, uint, bool> combinationTaken, Func<IpcMessage, Task<bool>>? send = null)
    {
        _parent = parent; _settings = settings; _combinationTaken = combinationTaken;
        _client = App.HookClient;
        _send = send ?? (msg => _client?.TrySendMessageAsync(msg) ?? Task.FromResult(false));
        _baseline = QuickPanelWakeBindings.Get(settings.Hotkeys).Select(b => b.Copy()).ToList();
        _baselineDesktop = parent.QuickPanelMouseAllowDesktop;
        _baselineProcesses = parent.MouseWakeWhitelist.ToSettingsList();
        foreach (var binding in _baseline) AddRow(binding);
        Rows.CollectionChanged += (_, _) => Changed();
        MouseWhitelist.Items.CollectionChanged += WhitelistChanged;
        AddCommand = new RelayCommand(AddPreset);
        RemoveCommand = new RelayCommand<QuickPanelWakeBindingViewModel>(Remove);
        UndoCommand = new RelayCommand(Undo, () => IsDirty);
        TestCommand = new RelayCommand<QuickPanelWakeBindingViewModel>(row => _ = StartTestAsync(row), row => row != null && QuickPanelWakeBindings.Validate(row.Export()) == null);
        StopTestCommand = new RelayCommand(() => StopTest(), () => IsTesting);
        _testTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (--_remaining <= 0) StopTest();
            OnPropertyChanged(nameof(StopTestLabel));
        }, _dispatcher);
        _testTimer.Stop();
        if (_client != null) _client.OnQuickPanelWakeTestResult += HandleResult;
        TranslationManager.Instance.PropertyChanged += LanguageChanged;
        _parent.PropertyChanged += ParentChanged;
        Validate();
    }

    public ObservableCollection<QuickPanelWakeBindingViewModel> Rows { get; } = new();
    internal event Action? BindingsChanged;
    public ProcessBlacklistEditorViewModel MouseWhitelist => _parent.MouseWakeWhitelist;
    public bool AllowDesktop { get => _parent.QuickPanelMouseAllowDesktop; set { if (value == AllowDesktop) return; _parent.QuickPanelMouseAllowDesktop = value; OnPropertyChanged(); Changed(); } }
    public bool ExtraProgramsExpanded { get => _parent.MouseWakeExtraProgramsExpanded; set { _parent.MouseWakeExtraProgramsExpanded = value; OnPropertyChanged(); } }
    private bool _scopeExpanded;
    public bool ScopeExpanded { get => _scopeExpanded; set => SetProperty(ref _scopeExpanded, value); }
    public int SelectedPreset { get; set; }
    public IReadOnlyList<WakeChoice<int>> Presets => new[] { "Keyboard", "RightHold", "MiddleClick", "BackClick", "ForwardClick", "Circle" }
        .Select((key, index) => new WakeChoice<int>(index, TranslationManager.Instance["Wake_Preset" + key])).ToArray();
    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand StopTestCommand { get; }
    public bool HasRows => Rows.Count > 0;
    public string Error { get => _error; private set { SetProperty(ref _error, value); OnPropertyChanged(nameof(IsValid)); } }
    public bool IsValid => string.IsNullOrEmpty(Error);
    public bool IsDirty => JsonSerializer.Serialize(Export()) != JsonSerializer.Serialize(_baseline)
        || AllowDesktop != _baselineDesktop || !MouseWhitelist.ToSettingsList().SequenceEqual(_baselineProcesses, StringComparer.OrdinalIgnoreCase);
    public bool IsTesting { get => _isTesting; private set => SetProperty(ref _isTesting, value); }
    public string TestMessage { get => _testMessage; private set => SetProperty(ref _testMessage, value); }
    public string StopTestLabel => string.Format(TranslationManager.Instance["Wake_StopTest"], _remaining);
    public string ScopeSummary => TranslationManager.Instance[AllowDesktop
        ? (MouseWhitelist.Items.Count > 0 ? "Wake_ScopeDesktopPrograms" : "Wake_ScopeDesktop")
        : (MouseWhitelist.Items.Count > 0 ? "Wake_ScopePrograms" : "Wake_ScopeNone")];
    public List<QuickPanelWakeBinding> Export() => Rows.Select(r => r.Export()).ToList();

    private void AddRow(QuickPanelWakeBinding binding)
    {
        var row = new QuickPanelWakeBindingViewModel(binding);
        row.PropertyChanged += RowChanged;
        Rows.Add(row);
    }
    private void AddPreset()
    {
        var binding = new QuickPanelWakeBinding { Enabled = false };
        switch (SelectedPreset)
        {
            case 1: binding.Kind = QuickPanelWakeKind.Hold; break;
            case 2: binding.Kind = QuickPanelWakeKind.Click; binding.Button = QuickPanelWakeButton.Middle; break;
            case 3: binding.Kind = QuickPanelWakeKind.Click; binding.Button = QuickPanelWakeButton.Back; break;
            case 4: binding.Kind = QuickPanelWakeKind.Click; binding.Button = QuickPanelWakeButton.Forward; break;
            case 5: binding.Kind = QuickPanelWakeKind.Circle; break;
        }
        AddRow(binding); Rows[^1].IsEditing = true;
    }
    private void Remove(QuickPanelWakeBindingViewModel row)
    { if (row == null) return; row.PropertyChanged -= RowChanged; Rows.Remove(row); }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(QuickPanelWakeBindingViewModel.Summary) or nameof(QuickPanelWakeBindingViewModel.DelayLabel)
            or nameof(QuickPanelWakeBindingViewModel.IsEditing) or nameof(QuickPanelWakeBindingViewModel.Name)
            or nameof(QuickPanelWakeBindingViewModel.Buttons) or nameof(QuickPanelWakeBindingViewModel.Directions))) Changed();
    }
    private void WhitelistChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Changed();
    private void ParentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HotkeySettingsViewModel.SelectedTab) && _parent.SelectedTab != "Global") StopTest();
        if (e.PropertyName is nameof(HotkeySettingsViewModel.ToggleHotkeyValue) or nameof(HotkeySettingsViewModel.QuickSwitchComboHotkey)
            or nameof(HotkeySettingsViewModel.QuickNavigationHotkey)) { Validate(); CommandManager.InvalidateRequerySuggested(); }
        if (e.PropertyName == nameof(HotkeySettingsViewModel.MouseWakeExtraProgramsExpanded)) OnPropertyChanged(nameof(ExtraProgramsExpanded));
    }
    private void Changed()
    {
        if (_restoring) return;
        StopTest(); Validate();
        OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(HasRows)); OnPropertyChanged(nameof(ScopeSummary));
        BindingsChanged?.Invoke();
        CommandManager.InvalidateRequerySuggested();
    }
    public bool Validate(bool probeSystem = false)
    {
        var rules = Export();
        var reserved = new[] { _parent.ToggleHotkeyValue, _parent.QuickSwitchComboHotkey, _parent.QuickNavigationHotkey,
            _parent.OpenFullWindowHotkey, _parent.LocalSendSendWindowHotkey, _parent.StayOpenHotkey,
            _parent.NextItemHotkey, _parent.PreviousItemHotkey, _parent.ActionsMenuHotkey, _parent.CompleteFromSelectionHotkey,
            _parent.QuickLookHotkey, _parent.KeywordHistoryPreviousHotkey, _parent.KeywordHistoryNextHotkey, _parent.KeywordHistoryDeleteHotkey }
            .Concat(_settings.Favorites.Select(f => f.Hotkey))
            .Concat(_settings.Hotkeys.PluginActionHotkeys.Values.SelectMany(actions => actions.Values))
            .Concat(_parent.PluginActionGroups.SelectMany(group => group.Items.Select(item => item.HotkeyValue)))
            .Concat(FavoriteHotkeyConflicts.DynamicPluginHotkeys(_settings))
            .ToArray();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (!rule.Enabled) continue;
            var problem = QuickPanelWakeBindings.Validate(rule);
            if (problem == null && rules.Take(i).Any(other => QuickPanelWakeBindings.Conflicts(rule, other))) problem = "Wake_Duplicate";
            if (problem == null && rule.Kind == QuickPanelWakeKind.Keyboard && reserved.Any(key => QuickPanelWakeBindings.SameKey(rule.Key, key))) problem = "Wake_Reserved";
            if (problem == null && probeSystem && rule.Kind == QuickPanelWakeKind.Keyboard
                && FavoriteHotkeyFormat.TryBuild(rule.Key, out var key, out var modifiers) && _combinationTaken(modifiers, key)) problem = "Wake_KeyTaken";
            if (problem != null)
            {
                Error = string.Format(TranslationManager.Instance["Wake_RowError"], i + 1, TranslationManager.Instance[problem]);
                return false;
            }
        }
        Error = ""; return true;
    }
    public void MarkSaved()
    {
        _baseline = Export(); _baselineDesktop = AllowDesktop; _baselineProcesses = MouseWhitelist.ToSettingsList();
        OnPropertyChanged(nameof(IsDirty));
    }
    public void Undo()
    {
        StopTest(); _restoring = true;
        foreach (var row in Rows) row.PropertyChanged -= RowChanged;
        Rows.Clear(); foreach (var binding in _baseline) AddRow(binding);
        _parent.QuickPanelMouseAllowDesktop = _baselineDesktop;
        MouseWhitelist.Items.Clear(); foreach (var name in _baselineProcesses) MouseWhitelist.Items.Add(new BlacklistProcessItem(name));
        _restoring = false; OnPropertyChanged(nameof(AllowDesktop)); Changed();
    }
    public async Task StartTestAsync(QuickPanelWakeBindingViewModel row)
    {
        if (_disposed || row == null || QuickPanelWakeBindings.Validate(row.Export()) != null) return;
        StopTest();
        var binding = row.Export(); binding.Id = Guid.NewGuid().ToString("N"); binding.Enabled = true;
        var id = _testId = binding.Id;
        _remaining = 30; _lastTestResult = ""; IsTesting = true;
        TestMessage = TranslationManager.Instance["Wake_TestConnecting"];
        OnPropertyChanged(nameof(StopTestLabel));
        var request = new QuickPanelWakeTestRequest { Binding = binding, AllowDesktop = AllowDesktop, AllowedProcesses = MouseWhitelist.ToSettingsList() };
        bool sent;
        try { sent = await _send(new() { Id = IpcMessageId.StartQuickPanelWakeTest, StringVal1 = JsonSerializer.Serialize(request), StringVal2 = id }); }
        catch { sent = false; }
        if (_disposed || _testId != id) return;
        if (!sent) { _testId = null; IsTesting = false; TestMessage = TranslationManager.Instance["Wake_TestUnavailable"]; }
        else _testTimer.Start();
    }
    private void HandleResult(string id, string reason) => _dispatcher.BeginInvoke(new Action(() => ReceiveTestResult(id, reason)));
    internal void ReceiveTestResult(string id, string reason)
    {
        if (_disposed || !IsTesting || id != _testId) return;
        _lastTestResult = TranslationManager.Instance[reason];
        TestMessage = _lastTestResult;
    }
    public void StopTest()
    {
        _testTimer?.Stop();
        if (_testId == null) return;
        _testId = null; IsTesting = false;
        _ = _send(new() { Id = IpcMessageId.StopQuickPanelWakeTest });
        TestMessage = string.IsNullOrEmpty(_lastTestResult) ? TranslationManager.Instance["Wake_TestFinished"]
            : _lastTestResult + " · " + TranslationManager.Instance["Wake_TestFinished"];
    }
    private void LanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var row in Rows) row.RefreshLabels();
        OnPropertyChanged(nameof(Presets)); OnPropertyChanged(nameof(ScopeSummary)); OnPropertyChanged(nameof(StopTestLabel)); Validate();
    }
    public void Dispose()
    {
        if (_disposed) return;
        StopTest(); _disposed = true;
        if (_client != null) _client.OnQuickPanelWakeTestResult -= HandleResult;
        TranslationManager.Instance.PropertyChanged -= LanguageChanged;
        _parent.PropertyChanged -= ParentChanged;
        MouseWhitelist.Items.CollectionChanged -= WhitelistChanged;
        foreach (var row in Rows) row.PropertyChanged -= RowChanged;
    }
}
