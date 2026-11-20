using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.Theme;

internal sealed record ThemeSelection(string ManualId, string LightId, string DarkId, bool FollowSystem)
{
    internal static ThemeSelection Read(UserSettings settings) =>
        new(settings.Theme, settings.LightThemeId, settings.DarkThemeId, settings.ThemeFollowSystem);

    internal void Store(UserSettings settings) =>
        (settings.Theme, settings.LightThemeId, settings.DarkThemeId, settings.ThemeFollowSystem) =
            (ManualId, LightId, DarkId, FollowSystem);

    internal string ActiveId(bool systemLight) => FollowSystem ? (systemLight ? LightId : DarkId) : ManualId;

    internal ThemeSelection Resolve(IReadOnlyList<ITheme> themes)
    {
        ITheme? Find(string id) => themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        return this with
        {
            ManualId = Find(ManualId)?.Id ?? (ManualId?.EndsWith("Dark", StringComparison.OrdinalIgnoreCase) == true ? "Dark" : "Light"),
            LightId = Find(LightId) is { IsDark: false } light ? light.Id : "Light",
            DarkId = Find(DarkId) is { IsDark: true } dark ? dark.Id : "Dark"
        };
    }
}
