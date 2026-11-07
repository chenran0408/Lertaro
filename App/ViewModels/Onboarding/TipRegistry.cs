using Lertaro.App.Services;

namespace Lertaro.App.ViewModels.Onboarding;

/// <summary>
/// D1: the point-of-use tip catalog. Each tip shows once (UserSettings.SeenTips), never while the
/// tour is active, and reuses the tour's card visuals. Copy comes from translation keys so all
/// seven languages ship together.
/// </summary>
internal static class TipRegistry
{
    public const string QuickSwitch = "quickswitch";
    public const string Clipboard = "clipboard";
    public const string Filters = "filters";

    public static bool TryGetCopy(string tipId, out string title, out string body)
    {
        switch (tipId)
        {
            case QuickSwitch:
                title = T("Tip_QuickSwitch_Title");
                body = T("Tip_QuickSwitch_Body");
                return true;
            case Clipboard:
                title = T("Tip_Clipboard_Title");
                body = T("Tip_Clipboard_Body");
                return true;
            case Filters:
                title = T("Tip_Filters_Title");
                body = T("Tip_Filters_Body");
                return true;
            default:
                title = body = string.Empty;
                return false;
        }
    }

    private static string T(string key) => TranslationManager.Instance[key];
}
