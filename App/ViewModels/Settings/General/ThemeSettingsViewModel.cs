using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

using Lertaro.App.Services.Theme;
namespace Lertaro.App.ViewModels.Settings.General;

/// <summary>Theme selection, including the optional "follow system light/dark" mode. Split out of
/// GeneralSettingsViewModel to keep that file under the project's line limit. Every change here
/// applies live (no Apply()/staging), matching how the manual theme pick has always worked.</summary>
public class ThemeSettingsViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;
    private ThemeOption? _selectedTheme;
    private ThemeOption? _selectedLightTheme;
    private ThemeOption? _selectedDarkTheme;
    private bool _followSystem;
    private IReadOnlyList<ThemeOption>? _themeOptions;
    private IReadOnlyList<ThemeOption>? _lightThemeOptions;
    private IReadOnlyList<ThemeOption>? _darkThemeOptions;
    private ICommand? _selectThemeModeCommand;

    public ThemeSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        _selectedTheme = ThemeOptions.FirstOrDefault(o => o.Value == _userSettings.Theme)
                         ?? ThemeOptions.FirstOrDefault();
        _selectedLightTheme = LightThemeOptions.FirstOrDefault(o => o.Value == _userSettings.LightThemeId)
                              ?? LightThemeOptions.FirstOrDefault();
        _selectedDarkTheme = DarkThemeOptions.FirstOrDefault(o => o.Value == _userSettings.DarkThemeId)
                             ?? DarkThemeOptions.FirstOrDefault();
        _followSystem = _userSettings.ThemeFollowSystem;

        // Dynamically refresh properties when the language changes -- ThemeOption.Label is a
        // TranslationService lookup (Theme_<Id>), so it genuinely changes text, unlike the option's
        // Id/Value. See GeneralSettingsViewModel's identical LogLevel/Language handling for why this
        // needs an explicit re-match-by-Value after the ItemsSource rebuild rather than relying on
        // record value-equality to "just work".
        _translationHandler = (s, e) =>
        {
            _background?.RefreshLanguage();
            _themeOptions = null;
            _lightThemeOptions = null;
            _darkThemeOptions = null;
            OnPropertyChanged(nameof(ThemeOptions));
            OnPropertyChanged(nameof(LightThemeOptions));
            OnPropertyChanged(nameof(DarkThemeOptions));

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var newTheme = ThemeOptions.FirstOrDefault(o => o.Value == _userSettings.Theme);
                if (newTheme != null) SelectedTheme = newTheme;

                var newLightTheme = LightThemeOptions.FirstOrDefault(o => o.Value == _userSettings.LightThemeId);
                if (newLightTheme != null) SelectedLightTheme = newLightTheme;

                var newDarkTheme = DarkThemeOptions.FirstOrDefault(o => o.Value == _userSettings.DarkThemeId);
                if (newDarkTheme != null) SelectedDarkTheme = newDarkTheme;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    public IReadOnlyList<ThemeOption> ThemeOptions => _themeOptions ??= SettingsOptionGenerator.GetThemeOptions();

    // Filtered to each half's own flavor -- a dark-flavored theme showing up as a candidate for the
    // "light" side (or vice versa) would defeat the point of "follow system" in the first place.
    public IReadOnlyList<ThemeOption> LightThemeOptions => _lightThemeOptions ??= SettingsOptionGenerator.GetThemeOptions(isDark: false);
    public IReadOnlyList<ThemeOption> DarkThemeOptions => _darkThemeOptions ??= SettingsOptionGenerator.GetThemeOptions(isDark: true);

    // ThemeOption itself doesn't carry an IsDark flag (it's just Value/Label), so this checks
    // membership in DarkThemeOptions -- record value-equality (by Value) makes Contains reliable here.
    private bool IsSelectedThemeDark => _selectedTheme != null && DarkThemeOptions.Contains(_selectedTheme);

    // Drives which of the three Theme Mode cards (Light / Dark / Follow System) shows as selected.
    public string ThemeModeTag => FollowSystem ? "FollowSystem" : IsSelectedThemeDark ? "Dark" : "Light";

    // Backs the three Theme Mode cards. Light/Dark select the GlassDesk palette directly; Follow
    // System tracks the OS light/dark mode.
    public ICommand SelectThemeModeCommand => _selectThemeModeCommand ??= new RelayCommand<string>(mode =>
    {
        switch (mode)
        {
            case "FollowSystem":
                FollowSystem = true;
                break;
            case "Dark":
                FollowSystem = false;
                if (SelectedDarkTheme != null) SelectedTheme = SelectedDarkTheme;
                break;
            default:
                FollowSystem = false;
                if (SelectedLightTheme != null) SelectedTheme = SelectedLightTheme;
                break;
        }

    });

    public ThemeOption? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value == null) return;
            if (_selectedTheme != value)
            {
                var isThemeIdChanged = _userSettings.Theme != value.Value;
                _selectedTheme = value;
                _userSettings.Theme = value.Value;
                _userSettings.Save();
                if (isThemeIdChanged)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }

                // Keep the per-flavor slots in sync with the active built-in mode.
                if (IsSelectedThemeDark) SelectedDarkTheme = value; else SelectedLightTheme = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(ThemeModeTag));
            }
        }
    }

    // Which theme "follow system" applies for light/dark, plus the on/off switch itself. Turning it
    // on switches to the resolved light/dark pick; turning it off switches back to the manually
    // selected theme (SelectedTheme's own setter never got a chance to apply while follow-system was
    // overriding it) -- either way, skip the ApplyTheme call (and its fade animation) entirely when
    // the target is already the active theme.
    public bool FollowSystem
    {
        get => _followSystem;
        set
        {
            if (_followSystem != value)
            {
                _followSystem = value;
                _userSettings.ThemeFollowSystem = value;
                _userSettings.Save();
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThemeModeTag));
                var targetThemeId = value
                    ? ThemeManager.Instance.ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, _userSettings)
                    : _userSettings.Theme;
                if (!string.Equals(targetThemeId, ThemeManager.Instance.CurrentThemeId, StringComparison.OrdinalIgnoreCase))
                {
                    ThemeManager.Instance.ApplyTheme(targetThemeId, saveSettings: false);
                }
            }
        }
    }

    public ThemeOption? SelectedLightTheme
    {
        get => _selectedLightTheme;
        set
        {
            if (value == null) return;
            if (_selectedLightTheme != value)
            {
                // ThemeOption is a record, so a language switch alone (re-translated Label, same Id)
                // already trips this inequality -- gate the actual re-apply on the Id, not the record,
                // or every language change would needlessly re-apply and fade the active theme.
                var isThemeIdChanged = _userSettings.LightThemeId != value.Value;
                _selectedLightTheme = value;
                _userSettings.LightThemeId = value.Value;
                _userSettings.Save();
                OnPropertyChanged();
                if (isThemeIdChanged && _followSystem && SystemThemeWatcher.IsSystemLight)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }
            }
        }
    }

    public ThemeOption? SelectedDarkTheme
    {
        get => _selectedDarkTheme;
        set
        {
            if (value == null) return;
            if (_selectedDarkTheme != value)
            {
                var isThemeIdChanged = _userSettings.DarkThemeId != value.Value;
                _selectedDarkTheme = value;
                _userSettings.DarkThemeId = value.Value;
                _userSettings.Save();
                OnPropertyChanged();
                if (isThemeIdChanged && _followSystem && !SystemThemeWatcher.IsSystemLight)
                {
                    ThemeManager.Instance.ApplyTheme(value.Value, saveSettings: false);
                }
            }
        }
    }

    private BackgroundSettingsViewModel? _background;
    public BackgroundSettingsViewModel Background => _background ??= new BackgroundSettingsViewModel(_userSettings);
    public void StageBackground() => _background?.Stage();
    public void AcceptBackground() => _background?.Accept();
    public void RejectBackgroundSave() => _background?.RejectSave();

    public void Cleanup()
    {
        TranslationManager.Instance.PropertyChanged -= _translationHandler;
        _background?.Cleanup();
    }
}
