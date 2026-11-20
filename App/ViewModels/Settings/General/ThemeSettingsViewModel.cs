using System.Windows.Threading;
using Lertaro.App.Services;
using Lertaro.App.Services.Theme;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.ViewModels.Settings.General;

public class ThemeSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _settings;
    private readonly IReadOnlyList<ITheme> _themes;
    private ThemeSelection _committed;
    private ThemeSelection _draft;
    private IReadOnlyList<ThemeOption>? _options;
    private IReadOnlyList<ThemeCardOption>? _cards;
    private BackgroundSettingsViewModel? _background;
    private string _selectedSection = "Theme";

    public ThemeSettingsViewModel(UserSettings settings)
    {
        _settings = settings;
        _themes = ThemeManager.Instance.GetAvailableThemes().OrderBy(t => t.Id).ToArray();
        _committed = _draft = ThemeSelection.Read(settings).Resolve(_themes);
        TranslationManager.Instance.PropertyChanged += OnLanguageChanged;
    }

    public IReadOnlyList<ThemeOption> ThemeOptions => _options ??= _themes.Select(t => new ThemeOption(t.Id, t.DisplayName)).ToArray();
    public IReadOnlyList<ThemeOption> LightThemeOptions => ThemeOptions.Where(o => !IsDark(o.Value)).ToArray();
    public IReadOnlyList<ThemeOption> DarkThemeOptions => ThemeOptions.Where(o => IsDark(o.Value)).ToArray();
    public IReadOnlyList<ThemeCardOption> ThemeCards => _cards ??= _themes.Select(t => new ThemeCardOption(t)).ToArray();
    public IReadOnlyList<ThemeCardOption> LightThemeCards => ThemeCards.Where(c => !c.IsDark).ToArray();
    public IReadOnlyList<ThemeCardOption> DarkThemeCards => ThemeCards.Where(c => c.IsDark).ToArray();
    public IReadOnlyList<ThemeCardOption> ManualThemeCards => IsDark(_draft.ManualId) ? DarkThemeCards : LightThemeCards;
    public string PreferredTheme => _draft.ManualId;
    public bool IsManualThemeEnabled => !_draft.FollowSystem;
    public string SelectedSection { get => _selectedSection; set => SetProperty(ref _selectedSection, value); }
    private bool IsDark(string id) => _themes.FirstOrDefault(t => t.Id == id)?.IsDark == true;
    private ThemeOption? Option(string? id) => ThemeOptions.FirstOrDefault(o => o.Value == id);

    public bool FollowSystem
    {
        get => _draft.FollowSystem;
        set => Update(_draft with { FollowSystem = value });
    }

    public string ThemeModeTag
    {
        get => FollowSystem ? "FollowSystem" : IsDark(_draft.ManualId) ? "Dark" : "Light";
        set
        {
            if (value == ThemeModeTag) return;
            switch (value)
            {
                case "FollowSystem": FollowSystem = true; break;
                case "Light": Update(_draft with { FollowSystem = false, ManualId = _draft.LightId }); break;
                case "Dark": Update(_draft with { FollowSystem = false, ManualId = _draft.DarkId }); break;
            }
        }
    }

    public ThemeOption? SelectedTheme
    {
        get => Option(_draft.ManualId);
        set
        {
            if (value == null || value.Value == _draft.ManualId) return;
            Update(IsDark(value.Value) ? _draft with { ManualId = value.Value, DarkId = value.Value }
                : _draft with { ManualId = value.Value, LightId = value.Value });
        }
    }

    public ThemeOption? SelectedLightTheme
    {
        get => Option(_draft.LightId);
        set { if (value != null && !IsDark(value.Value)) Update(_draft with { LightId = value.Value }); }
    }
    public ThemeOption? SelectedDarkTheme
    {
        get => Option(_draft.DarkId);
        set { if (value != null && IsDark(value.Value)) Update(_draft with { DarkId = value.Value }); }
    }

    public string? SelectedThemeId { get => _draft.ManualId; set => SelectedTheme = Option(value); }
    public string? SelectedLightThemeId { get => _draft.LightId; set => SelectedLightTheme = Option(value); }
    public string? SelectedDarkThemeId { get => _draft.DarkId; set => SelectedDarkTheme = Option(value); }

    private void Update(ThemeSelection selection)
    {
        if (selection == _draft) return;
        var mode = ThemeModeTag;
        _draft = selection;
        ThemeManager.Instance.Preview(selection, _themes);
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(SelectedLightTheme));
        OnPropertyChanged(nameof(SelectedDarkTheme));
        OnPropertyChanged(nameof(PreferredTheme));
        if (mode != ThemeModeTag)
        {
            OnPropertyChanged(nameof(FollowSystem));
            OnPropertyChanged(nameof(IsManualThemeEnabled));
            OnPropertyChanged(nameof(ThemeModeTag));
            OnPropertyChanged(nameof(ManualThemeCards));
            // A newly visible grid generates its containers after the mode selection returns.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(NotifySelectedIds, DispatcherPriority.Loaded);
        }
        NotifySelectedIds();
    }

    private void NotifySelectedIds()
    {
        OnPropertyChanged(nameof(SelectedThemeId));
        OnPropertyChanged(nameof(SelectedLightThemeId));
        OnPropertyChanged(nameof(SelectedDarkThemeId));
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _options = null;
        _cards = null;
        _background?.RefreshLanguage();
        OnPropertyChanged(string.Empty);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(NotifySelectedIds, DispatcherPriority.Loaded);
    }

    public BackgroundSettingsViewModel Background => _background ??= new BackgroundSettingsViewModel(_settings);

    public void Stage()
    {
        if (_draft != _committed) _draft.Store(_settings);
        _background?.Stage();
    }

    public void RejectSave()
    {
        if (_draft != _committed) _committed.Store(_settings);
        _background?.RejectSave();
    }

    public void Accept()
    {
        _committed = _draft;
        ThemeManager.Instance.EndPreview(_settings, _themes);
        _background?.Accept();
    }

    public void Cleanup()
    {
        TranslationManager.Instance.PropertyChanged -= OnLanguageChanged;
        ThemeManager.Instance.EndPreview(_settings, _themes);
        _background?.Cleanup();
    }
}
