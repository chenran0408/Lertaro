using System.Windows;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
using Lertaro.App.Helpers.Visuals;
namespace Lertaro.App.Services.Theme;

public class ThemeManager
{
    private static readonly Lazy<ThemeManager> _instance = new(() => new ThemeManager());
    private static readonly PluginSdk.Abstractions.ITheme[] GlassDeskThemes =
    [
        new BuiltInTheme("Light", false),
        new BuiltInTheme("Dark", true)
    ];
    public static ThemeManager Instance => _instance.Value;

    private ResourceDictionary? _activeThemeDictionary;
    private string _currentThemeId = "Light";
    private PluginSdk.Abstractions.ITheme? _activeTheme;

    public string CurrentThemeId => _currentThemeId;
    public ResourceDictionary? ActiveThemeDictionary => _activeThemeDictionary;
    public PluginSdk.Abstractions.ITheme? ActiveTheme => _activeTheme;

    public event Action? ThemeChanged;

    private ThemeManager() => PluginSdk.Services.ThemeService.IsDarkThemeFunc = () => _activeTheme?.IsDark ?? false;

    // Color preset packs paint over the sampled GlassDesk surface, so the host now exposes only
    // GlassDesk's two palette modes. Installed theme-pack files are not removed; their colors are
    // no longer selectable or applied.
    public IEnumerable<PluginSdk.Abstractions.ITheme> GetAvailableThemes() => GlassDeskThemes;

    public void Initialize(string preferredThemeId)
    {
        var settings = UserSettings.Load();
        var themeIds = new[] { preferredThemeId, settings.Theme, settings.LightThemeId, settings.DarkThemeId };
        List<PluginSdk.Abstractions.ITheme> legacyThemes = themeIds.Any(NeedsLegacyThemeLookup)
            ? GetLegacyThemes().ToList() : [];
        var activeMode = ResolveGlassDeskMode(preferredThemeId, SystemThemeWatcher.IsSystemLight ? "Light" : "Dark", legacyThemes);
        var storedMode = ResolveGlassDeskMode(settings.Theme, "Light", legacyThemes);
        var lightMode = ResolveGlassDeskMode(settings.LightThemeId, "Light", legacyThemes);
        var darkMode = ResolveGlassDeskMode(settings.DarkThemeId, "Dark", legacyThemes);

        // The per-system slots are semantic slots: a legacy dark preset cannot remain assigned to
        // the light slot (or vice versa) after its colors are removed.
        if (lightMode != "Light") lightMode = "Light";
        if (darkMode != "Dark") darkMode = "Dark";

        if (settings.Theme != storedMode || settings.LightThemeId != lightMode || settings.DarkThemeId != darkMode)
        {
            settings.Theme = storedMode;
            settings.LightThemeId = lightMode;
            settings.DarkThemeId = darkMode;
            settings.Save();
        }

        ApplyTheme(activeMode, saveSettings: false);
    }

    private static IEnumerable<PluginSdk.Abstractions.ITheme> GetLegacyThemes() => PluginManager.Instance.ThemeProviders
        .SelectMany(provider => PluginPerformanceMonitor.Measure(provider,
            () => provider.GetThemes()?.ToList() ?? new List<PluginSdk.Abstractions.ITheme>()));

    private static bool NeedsLegacyThemeLookup(string? themeId) =>
        !string.Equals(themeId, "Light", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(themeId, "Dark", StringComparison.OrdinalIgnoreCase)
        && themeId?.EndsWith("Light", StringComparison.OrdinalIgnoreCase) != true
        && themeId?.EndsWith("Dark", StringComparison.OrdinalIgnoreCase) != true;

    internal static string ResolveGlassDeskMode(string? themeId, string fallback,
        IEnumerable<PluginSdk.Abstractions.ITheme> legacyThemes)
    {
        if (string.Equals(themeId, "Light", StringComparison.OrdinalIgnoreCase)) return "Light";
        if (string.Equals(themeId, "Dark", StringComparison.OrdinalIgnoreCase)) return "Dark";

        var legacy = legacyThemes.FirstOrDefault(theme =>
            string.Equals(theme.Id, themeId, StringComparison.OrdinalIgnoreCase));
        if (legacy != null) return legacy.IsDark ? "Dark" : "Light";

        if (themeId?.EndsWith("Dark", StringComparison.OrdinalIgnoreCase) == true) return "Dark";
        if (themeId?.EndsWith("Light", StringComparison.OrdinalIgnoreCase) == true) return "Light";
        return string.Equals(fallback, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
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
                var settings = UserSettings.Load();
                if (!settings.ThemeFollowSystem) return;
                ApplyTheme(ResolveLightDarkThemeId(SystemThemeWatcher.IsSystemLight, settings), saveSettings: false);
            }));
        };
    }

    /// <summary>Resolves the "follow system" light/dark theme pair's configured Id to one that
    /// actually exists among the GlassDesk palette modes. Retired preset IDs, missing IDs, or IDs
    /// with the wrong light/dark flavor resolve to the matching GlassDesk mode.</summary>
    public string ResolveLightDarkThemeId(bool wantLight, UserSettings? settings = null)
    {
        settings ??= UserSettings.Load();
        var configured = wantLight ? settings.LightThemeId : settings.DarkThemeId;
        var themes = GetAvailableThemes().Where(t => t.IsDark != wantLight).ToList();
        if (!string.IsNullOrEmpty(configured) && themes.Any(t => string.Equals(t.Id, configured, StringComparison.OrdinalIgnoreCase)))
        {
            return configured;
        }
        return themes.FirstOrDefault()?.Id ?? "Light";
    }

    public bool ApplyTheme(string themeId, bool saveSettings = true)
    {
        var themes = GetAvailableThemes().ToList();
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
