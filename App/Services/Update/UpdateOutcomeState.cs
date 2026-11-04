namespace Lertaro.App.Services.Update;

/// <summary>
/// The one-line verdict of the version transition detected at startup ("Updated from 6.4.0 to
/// 6.4.1"), shown on the About page. Set once per App start before any UI opens; the About page
/// only reads it. Empty when there is nothing to report (fresh install, same version as last run).
/// </summary>
public static class UpdateOutcomeState
{
    /// <summary>Written on the UI thread during startup; read by the About page on the UI thread.</summary>
    public static string? Summary { get; private set; }

    /// <summary>Pure transition rule: an empty recorded version is a fresh install (nothing to
    /// report), a match is business as usual, anything else is a transition worth one line.</summary>
    internal static bool HasTransition(string? lastSeenVersion, string currentVersion)
        => !string.IsNullOrWhiteSpace(lastSeenVersion) && lastSeenVersion != currentVersion;

    public static void Report(string previousVersion, string currentVersion) => Summary = string.Format(TranslationManager.Instance["About_UpdateSucceeded"], previousVersion, currentVersion);

    /// <summary>Clears the summary once it has been displayed, so it does not outlive its meaning
    /// within a long-running session. Not persisted: the transition is reported once.</summary>
    public static void Consume() => Summary = null;
}
