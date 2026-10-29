using System.Windows;
using Lertaro.App.Services.Theme;

namespace Lertaro.App.Tests.Views.Settings;

// Settings XAML uses application-level StaticResource fallbacks from the active palette. Tests that
// construct a page without starting App must install the same base dictionaries before InitializeComponent.
internal sealed class SettingsThemeFixture : IDisposable
{
    private SettingsThemeFixture() { }

    public static SettingsThemeFixture Use(string themeId = "Light", bool isDark = false)
    {
        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AddApplicationDictionary(application, "Resources/Styles.xaml");
        AddApplicationDictionary(application, "Resources/DataTemplates.xaml");
        if (!application.Resources.MergedDictionaries.Any(IsBuiltInTheme))
            application.Resources.MergedDictionaries.Add(new BuiltInTheme(themeId, isDark).GetResources());
        return new SettingsThemeFixture();
    }

    public void Dispose() { }

    private static bool IsBuiltInTheme(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString.EndsWith("/Resources/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true
        || dictionary.Source?.OriginalString.EndsWith("/Resources/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true;

    private static void AddApplicationDictionary(Application application, string path)
    {
        var source = new Uri($"pack://application:,,,/Lertaro.App;component/{path}", UriKind.Absolute);
        if (application.Resources.MergedDictionaries.Any(dictionary => dictionary.Source == source))
            return;
        application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
    }
}
