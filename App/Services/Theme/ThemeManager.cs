using System.Windows;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.Core;
using Lertaro.App.Services.Plugin;
using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Services.Theme;

public class ThemeManager
{
    private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
    private static readonly ITheme[] BuiltInThemes =
    [
        new BuiltInTheme("Light", false),
        new BuiltInTheme("Dark", true)
    ];
    public static ThemeManager Instance => _instance.Value;

    private ThemeSelection? _preview;
    private ResourceDictionary? _activeThemeDictionary;
    private string _currentThemeId = "Light";
    private ITheme? _activeTheme;

    public string CurrentThemeId => _currentThemeId;
    public ResourceDictionary? ActiveThemeDictionary => _activeThemeDictionary;
    public ITheme? ActiveTheme => _activeTheme;

    public event Action? ThemeChanged;

    private ThemeManager() => PluginSdk.Services.ThemeService.IsDarkThemeFunc = () => _activeTheme?.IsDark ?? false;

    public IEnumerable<ITheme> GetAvailableThemes() => PluginManager.Instance.ThemeProviders
            .SelectMany(p => PluginPerformanceMonitor.Measure(p, () => p.GetThemes()?.ToList() ?? new List<ITheme>()))
            .Concat(BuiltInThemes)
            .GroupBy(t => t.Id)
            .Select(g => g.First());

    public void Initialize(UserSettings settings)
    {
        var themes = GetAvailableThemes().ToArray();
        var saved = ThemeSelection.Read(settings);
        var resolved = saved.Resolve(themes);
        if (resolved != saved)
        {
            resolved.Store(settings);
            settings.Save();
        }
        ApplyTheme(resolved.ActiveId(SystemThemeWatcher.IsSystemLight), false, themes);
    }

    internal void Preview(ThemeSelection selection, IReadOnlyList<ITheme> themes)
    {
        _preview = selection;
        ApplySelection(selection, themes);
    }

    internal void EndPreview(UserSettings settings, IReadOnlyList<ITheme> themes)
    {
        if (_preview == null) return;
        _preview = null;
        ApplySelection(ThemeSelection.Read(settings), themes);
    }

    private void ApplySelection(ThemeSelection selection, IReadOnlyList<ITheme>? themes = null)
    {
        var themeId = selection.ActiveId(SystemThemeWatcher.IsSystemLight);
        if (!string.Equals(CurrentThemeId, themeId, StringComparison.OrdinalIgnoreCase))
            ApplyTheme(themeId, false, themes ?? GetAvailableThemes().ToArray());
    }

    /// <summary>Starts watching the OS light/dark setting and re-applies the user's configured
    /// light/dark theme pair whenever it flips, but only while ThemeFollowSystem is actually on --
    /// checked fresh off UserSettings each time rather than tracked locally, so toggling the setting
    /// off doesn't need a matching unsubscribe.</summary>
    public void InitializeSystemFollow()
    {
        SystemThemeWatcher.EnsureWatching();
        SystemThemeWatcher.SystemThemeChanged += () =>
        {
            // SystemEvents raises this on a non-UI thread. ApplyTheme touches WPF windows/dictionaries,
            // so marshal the whole handler body onto the Dispatcher before doing any theme work.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                var selection = _preview ?? ThemeSelection.Read(UserSettings.Load());
                if (selection.FollowSystem) ApplySelection(selection);
            }));
        };
    }

    public bool ApplyTheme(string themeId, bool saveSettings = true) =>
        ApplyTheme(themeId, saveSettings, GetAvailableThemes().ToArray());

    private bool ApplyTheme(string themeId, bool saveSettings, IReadOnlyList<ITheme> themes)
    {
        // Fallback to Light if not found
        var theme = themes.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase)) ?? themes.FirstOrDefault(t => string.Equals(t.Id, "Light", StringComparison.OrdinalIgnoreCase));
        if (theme == null)
        {
            Logger.Log($"[ThemeManager] No themes found, failed to apply theme '{themeId}'", LogLevel.Error);
            return false;
        }

        try
        {
            var newDict = theme.GetResources();

            var app = System.Windows.Application.Current;
            if (app == null || !app.Dispatcher.CheckAccess() || app.Dispatcher.HasShutdownStarted) return false;
            var dictionaries = app.Resources.MergedDictionaries;
            if (!ReferenceEquals(newDict, _activeThemeDictionary))
            {
                // Commit one palette on the UI thread. No delayed swap can overwrite a later choice.
                dictionaries.Add(newDict);
                if (_activeThemeDictionary != null) dictionaries.Remove(_activeThemeDictionary);
                _activeThemeDictionary = newDict;
            }
            _currentThemeId = theme.Id;
            _activeTheme = theme;
            foreach (Window window in app.Windows)
            {
                if (window.Content is UIElement content) content.BeginAnimation(UIElement.OpacityProperty, null);
                WindowEffectHelper.ApplyThemeEffects(window, theme);
            }
            ThemeChanged?.Invoke();

            Logger.Log($"[ThemeManager] Theme applied successfully: '{theme.DisplayName}' (Dark: {theme.IsDark})");

            if (saveSettings)
            {
                var settings = UserSettings.Load();
                settings.Theme = _currentThemeId;
                settings.Save();
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ThemeManager] Error applying theme '{themeId}': {ex.Message}", LogLevel.Error);
            return false;
        }
    }
}
