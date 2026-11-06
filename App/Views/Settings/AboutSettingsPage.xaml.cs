using System.IO;
using System.Windows;
using System.ComponentModel;
using Lertaro.App.Services;
using Lertaro.Core;
using MessageBox = Lertaro.App.Views.Controls.Dialogs.CustomMessageBox;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

using Lertaro.App.Services.Update;

using Lertaro.App.Services.Theme;
using Lertaro.Core.Services.Search;
namespace Lertaro.App.Views.Settings;

public partial class AboutSettingsPage : System.Windows.Controls.UserControl, INotifyPropertyChanged
{
    private GitHubReleaseInfo? _latestRelease;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    // Tracks which brush key is currently showing, since ServiceStatusBrush is a plain bound CLR
    // property (not a DependencyProperty) -- unlike SetResourceReference on a UI element, its value
    // never re-resolves on its own when the theme changes, so ThemeChanged below re-runs this lookup
    // manually against whatever status was last determined.
    private readonly PropertyChangedEventHandler _translationHandler;
    private string? _serviceStatusBrushKey;
    private Brush _serviceStatusBrush = Brushes.Gray;
    public Brush ServiceStatusBrush
    {
        get => _serviceStatusBrush;
        private set
        {
            if (_serviceStatusBrush != value)
            {
                _serviceStatusBrush = value;
                OnPropertyChanged(nameof(ServiceStatusBrush));
            }
        }
    }

    public string AppVersion => string.Format(TranslationManager.Instance["About_Version"], UpdateVersion.Current);

    public string CoreVersion
    {
        get
        {
            var version = typeof(Logger).Assembly.GetName().Version;
            return string.Format(TranslationManager.Instance["About_CoreVersion"], version?.ToString(3));
        }
    }

    public string ServiceVersion
        => AboutComponentVersionSupport.GetServiceVersion();

    public string CliVersion
        => AboutComponentVersionSupport.GetCliVersion();

    // The user manual link's target differs per locale (/zh-CN/ prefix), so it's derived from the
    // same translated URL string the link displays rather than a single hardcoded Uri like the
    // (locale-independent) homepage link above it.
    public Uri? UserGuideUri => Uri.TryCreate(TranslationManager.Instance["About_UserGuideUrl"], UriKind.Absolute, out var uri) ? uri : null;

    // %LocalAppData%\Lertaro -- the UI's own per-user data (user-settings.json and its .bak.N
    // rotation, UI logs). See Logger.UserDataDir's own doc comment.
    public string UserConfigDirPath => Logger.UserDataDir;

    // %ProgramData%\Lertaro -- the background service's machine-wide data (machine-settings.json,
    // service logs, index cache), shared across every user account on this machine. See
    // Logger.SharedDataDir's own doc comment.
    public string SystemConfigDirPath => Logger.SharedDataDir;

    public AboutSettingsPage()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += AboutSettingsPage_Loaded;

        // AppVersion/CoreVersion/ServiceVersion embed a translated format string, so they need to
        // re-resolve (not just the XAML-bound About_* labels) when the language changes at runtime.
        _translationHandler = (s, e) =>
        {
            OnPropertyChanged(nameof(AppVersion));
            OnPropertyChanged(nameof(CoreVersion));
            OnPropertyChanged(nameof(ServiceVersion));
            OnPropertyChanged(nameof(CliVersion));
            OnPropertyChanged(nameof(UserGuideUri));
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

        ThemeManager.Instance.ThemeChanged += UpdateServiceStatusBrush;
        Unloaded += (_, _) =>
        {
            ThemeManager.Instance.ThemeChanged -= UpdateServiceStatusBrush;
            TranslationManager.Instance.PropertyChanged -= _translationHandler;
        };
    }

    private void AboutSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ShowUpdateOutcome();
        ChkAutoCheckUpdates.IsChecked = UserSettings.Load().AutoCheckUpdates;
        CheckServiceStatus();
    }

    /// <summary>Shows the version-transition verdict once, then clears the static state so a later
    /// page visit does not repeat it within the same session.</summary>
    private void ShowUpdateOutcome()
    {
        if (UpdateOutcomeState.Summary is not { } summary) return;
        TxtUpdateOutcome.Text = summary;
        TxtUpdateOutcome.Visibility = Visibility.Visible;
        UpdateOutcomeState.Consume();
    }

    private void ChkAutoCheckUpdates_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkAutoCheckUpdates.IsChecked is not { } state) return;
        var settings = UserSettings.Load();
        if (settings.AutoCheckUpdates == state) return;
        settings.AutoCheckUpdates = state;
        _ = settings.Save();
    }

    private async void CheckServiceStatus()
    {
        try
        {
            using var searchService = new SearchService();
            var status = await searchService.GetStatusAsync();
            _serviceStatusBrushKey = status != null && status.State != "error" ? "SuccessBadgeText" : "ErrorBrush";
        }
        catch
        {
            _serviceStatusBrushKey = "ErrorBrush";
        }
        UpdateServiceStatusBrush();
    }

    private void UpdateServiceStatusBrush()
    {
        if (_serviceStatusBrushKey == null) return;
        var fallback = _serviceStatusBrushKey == "ErrorBrush" ? Brushes.Red : Brushes.Green;
        ServiceStatusBrush = System.Windows.Application.Current.TryFindResource(_serviceStatusBrushKey) as Brush ?? fallback;
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        _latestRelease = null;
        BtnDownloadUpdate.Visibility = Visibility.Collapsed;
        TxtUpdateStatus.Text = TranslationManager.Instance["About_Checking"];
        SpUpdateActions.Visibility = Visibility.Collapsed;

        GitHubReleaseInfo? release = null;
        try
        {
            release = await UpdateChecker.Instance.CheckForUpdatesAsync();
            BtnCheckUpdate.IsEnabled = true;

            if (release == null)
            {
                TxtUpdateStatus.Text = TranslationManager.Instance["About_UpdateUnavailable"];
                SpUpdateActions.Visibility = Visibility.Visible;
                return;
            }

            _latestRelease = release;
        }
        catch (Exception ex)
        {
            Logger.Log($"[AboutSettingsPage] Update check failed: {ex}", LogLevel.Warn);
            BtnCheckUpdate.IsEnabled = true;
            TxtUpdateStatus.Text = TranslationManager.Instance[ex is UpdateCheckException { RateLimited: true }
                ? "About_UpdateBusy" : "About_UpdateUnavailable"];
            SpUpdateActions.Visibility = Visibility.Visible;
            return;
        }

        // Compare versions
        if (UpdateVersion.Compare(release.TagName, UpdateVersion.Current) is { } comparison)
        {
            if (comparison > 0)
            {
                var newVerFormat = TranslationManager.Instance["About_NewVersionAvailable"];
                TxtUpdateStatus.Text = string.Format(newVerFormat, release.TagName);

                // Show update actions
                SpUpdateActions.Visibility = Visibility.Visible;
                BtnDownloadUpdate.Visibility = Visibility.Visible;

                // The service applies signed packages as LocalSystem, so a standard user can update
                // from this button without elevating the App or opening a download page.
                BtnDownloadUpdate.IsEnabled = true;
            }
            else
            {
                TxtUpdateStatus.Text = TranslationManager.Instance["About_UpToDate"];
            }
        }
        else
        {
            TxtUpdateStatus.Text = TranslationManager.Instance["About_UpdateUnavailable"];
            SpUpdateActions.Visibility = Visibility.Visible;
        }
    }

    private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_latestRelease == null) return;

        // Installer installs take the setup executable; portable installs take the zip (they run it
        // by extracting over their folder, so the guidance differs). One download, no service handoff,
        // no file replacement while this app runs.
        var serviceHere = ServiceInstallManager.IsInstalledAtCurrentPath();
        var asset = serviceHere
            ? UpdateAssetSelector.SelectSetup(_latestRelease.Assets, a => a.Name)
            : UpdateAssetSelector.SelectPortableZip(_latestRelease.Assets, a => a.Name);
        if (asset == null)
        {
            TxtUpdateStatus.Text = TranslationManager.Instance["About_Failed"];
            return;
        }

        BtnCheckUpdate.IsEnabled = false;
        BtnDownloadUpdate.IsEnabled = false;
        BtnGoToPage.IsEnabled = false;
        PbUpdate.Visibility = Visibility.Visible;
        PbUpdate.Value = 0;

        var downloadingFormat = TranslationManager.Instance["About_Downloading"];
        try
        {
            var filePath = await UpdateSetupDownloader.Instance.DownloadAsync(asset.BrowserDownloadUrl, asset.Name,
                progress => Dispatcher.Invoke(() =>
                {
                    PbUpdate.Value = progress * 100;
                    TxtUpdateStatus.Text = string.Format(downloadingFormat, (int)(progress * 100));
                }));

            PbUpdate.Visibility = Visibility.Collapsed;
            TxtUpdateStatus.Text = TranslationManager.Instance["About_Downloaded"];
            OfferManualInstall(filePath, serviceHere);
        }
        catch (Exception ex)
        {
            Logger.Log($"[AboutSettingsPage] Update download failed: {ex.Message}", LogLevel.Warn);
            PbUpdate.Visibility = Visibility.Collapsed;
            TxtUpdateStatus.Text = TranslationManager.Instance["About_Failed"];
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
            BtnDownloadUpdate.IsEnabled = true;
            BtnGoToPage.IsEnabled = true;
        }
    }

    /// <summary>
    /// The post-download guidance: where the package landed and what to do with it. Running the
    /// installer never exits this app -- the installer handles file locks itself (it closes the
    /// running instances), so the app is never the one to disappear.
    /// </summary>
    private void OfferManualInstall(string filePath, bool installerInstall)
    {
        var guidance = string.Format(
            TranslationManager.Instance[installerInstall ? "About_ManualInstallSetup" : "About_ManualInstallZip"],
            filePath);
        var dialog = new Controls.Dialogs.UpdateDialogWindow(
            TranslationManager.Instance["About_CheckUpdate"], guidance, showNeverPrompt: false);
        dialog.SetButtons(
            installerInstall ? TranslationManager.Instance["About_RunInstaller"] : TranslationManager.Instance["About_OpenFolder"],
            TranslationManager.Instance["About_OpenFolder"]);
        dialog.ShowDialog();

        switch (dialog.Result)
        {
            case Controls.Dialogs.UpdateDialogResult.Primary when installerInstall:
                TryLaunchInstaller(filePath);
                break;
            case Controls.Dialogs.UpdateDialogResult.Secondary when installerInstall:
            case Controls.Dialogs.UpdateDialogResult.Primary:
                OpenContainingFolder(filePath);
                break;
        }
    }

    private void TryLaunchInstaller(string filePath)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[AboutSettingsPage] Could not launch the installer: {ex.Message}", LogLevel.Warn);
            MessageBox.Show(
                TranslationManager.Instance["About_LaunchFailed"], TranslationManager.Instance["About_CheckUpdate"],
                MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenContainingFolder(filePath);
        }
    }

    private void OpenContainingFolder(string filePath)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }
        catch (Exception ex)
        {
            Logger.Log($"[AboutSettingsPage] Could not open the folder: {ex.Message}", LogLevel.Warn);
        }
    }

    private void BtnGoToPage_Click(object sender, RoutedEventArgs e) => Helpers.UrlLauncher.Open(_latestRelease?.HtmlUrl ?? UpdateChecker.ReleasesPageUrl);

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Helpers.UrlLauncher.Open(e.Uri);
        e.Handled = true;
    }

    private void OpenUserConfigDir_Click(object sender, RoutedEventArgs e) => OpenFolder(UserConfigDirPath);
    private void OpenSystemConfigDir_Click(object sender, RoutedEventArgs e) => OpenFolder(SystemConfigDirPath);

    // Creates the folder first -- SystemConfigDirPath in particular may not exist yet on a machine
    // where the background service was never installed/run, and opening a missing path would just
    // show Explorer's own "location not available" error instead of a usable, if empty, folder.
    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            // Through the app's own folder route rather than Process.Start, so the configured default file
            // manager and the "open in a new tab" option apply to these two buttons as well.
            FileExecutor.OpenFileOrFolder(path);
        }
        catch (Exception ex)
        {
            Logger.Log($"[AboutSettingsPage] OpenFolder failed for '{path}': {ex}", LogLevel.Error);
            MessageBox.Show(string.Format(TranslationManager.Instance["Executor_OpenFailed"], ex.Message), TranslationManager.Instance["Service_Error"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Config Management card: the flows (file pickers, confirms, message boxes) live in
    // UserConfigBackups to keep this page under the repo's per-file line limit.
    private async void BtnExportConfig_Click(object sender, RoutedEventArgs e) => await UserConfigBackups.RunExportFlowAsync(DataContext as ViewModels.Settings.SettingsViewModel);

    private async void BtnImportConfig_Click(object sender, RoutedEventArgs e) => await UserConfigBackups.RunImportFlowAsync();

    private async void BtnRestoreConfig_Click(object sender, RoutedEventArgs e) => await UserConfigBackups.RunRestoreFlowAsync();

    private void BtnOnboardingTour_Click(object sender, RoutedEventArgs e) =>
        Onboarding.OnboardingWindow.ShowTour();
}
