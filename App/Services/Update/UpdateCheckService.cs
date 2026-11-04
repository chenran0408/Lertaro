using System.Windows;
using Lertaro.Core;
using Lertaro.Core.Services.Update;
using Application = System.Windows.Application;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;

using Lertaro.App.Services.Tray;
namespace Lertaro.App.Services.Update;

/// <summary>
/// Startup update-check flow: checks GitHub for a new release and, depending on settings, either downloads
/// and installs it without asking anything (auto-silent-update enabled, service installed at this path) or
/// prompts the user to open the About page.
/// </summary>
/// <remarks>
/// Extracted out of App.OnStartup so that startup sequencing there doesn't carry this feature's own
/// background/UI flow inline.
/// </remarks>
public static class UpdateCheckService
{
    public static void RunOnStartupAsync() => _ = Task.Run(RunAsync);

    private static async Task RunAsync()
    {
        try
        {
            // Delay slightly to ensure app is fully initialized and main window is up
            await Task.Delay(3000);
            UpdateArtifactCleanup.Run();
            var settings = UserSettings.Load();

            // Version-transition verdict: the previous run recorded its version, so a difference
            // means the user completed a manual update and deserves the one-line confirmation the
            // old flow never gave. Recorded and persisted before anything else can fail; shown on
            // the About page. LastSeenVersion empty means first run -- recorded silently.
            if (UpdateOutcomeState.HasTransition(settings.LastSeenVersion, UpdateVersion.Current))
                UpdateOutcomeState.Report(settings.LastSeenVersion, UpdateVersion.Current);
            if (settings.LastSeenVersion != UpdateVersion.Current)
            {
                settings.LastSeenVersion = UpdateVersion.Current;
                _ = settings.Save();
            }

            if (!settings.AutoCheckUpdates)
                return;

            var release = await UpdateChecker.Instance.CheckForUpdatesAsync();
            if (release == null)
                return;

            if (!UpdateVersion.IsNewer(release.TagName, UpdateVersion.Current))
                return;

            // The download itself is a user action now: the About page downloads the setup and the
            // user runs it. A startup check only announces the release, so a network hiccup during a
            // 100 MB pull can no longer leave an app half-updated and a service stopped.
            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var promptText = string.Format(
                    TranslationManager.Instance["About_NewVersionAvailablePrompt"], release.TagName);
                var dialog = new Views.Controls.Dialogs.UpdateDialogWindow(
                    TranslationManager.Instance["About_CheckUpdate"], promptText, showNeverPrompt: true);
                dialog.SetButtons(TranslationManager.Instance["About_OpenAboutPage"]);
                dialog.ShowDialog();
                if (dialog.NeverPromptAgain)
                {
                    settings.AutoCheckUpdates = false;
                    _ = settings.Save();
                }
                if (dialog.Result == Views.Controls.Dialogs.UpdateDialogResult.Primary)
                    App.ShowSettingsWindow("About");
            }));
        }
        catch (Exception ex)
        {
            Logger.Log($"[App] Background startup update check failed: {ex.Message}", LogLevel.Warn);
        }
    }

    // A release tag ("v1.2.3") counts as newer only if it parses as a version strictly greater than
    // currentVersion -- an unparseable tag or a same-or-older one must never trigger the update prompt.
    internal static bool IsNewerVersion(string tagName, Version? currentVersion, out Version? latestVersion)
    {
        var cleanTag = tagName.TrimStart('v', 'V');
        return Version.TryParse(cleanTag, out latestVersion) && latestVersion > currentVersion;
    }

    internal static bool ShouldOpenAbout(MessageBoxResult result) => result == MessageBoxResult.OK;


}
