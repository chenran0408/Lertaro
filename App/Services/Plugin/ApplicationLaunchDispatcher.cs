using Lertaro.PluginSdk.Abstractions;
using System.Diagnostics;
using System.IO;

namespace Lertaro.App.Services.Plugin;

// Application providers use ShellExecute, which can wait indefinitely on a shell extension or DDE.
// Keep their callbacks on the existing STA workers, and marshal only view changes back to the UI.
internal static class ApplicationLaunchDispatcher
{
    internal static void OpenExternal(ISearchResult result, IPluginSearchWindow view, bool asAdmin)
    {
        if (result is AppSearchResult { IsApplication: true } application) Execute(application, view, asAdmin);
        else if (asAdmin) view.OpenFileOrFolderAsAdminExternal(result.FullPath);
        else view.OpenFileOrFolderExternal(result.FullPath);
    }

    internal static bool Execute(AppSearchResult result, IPluginSearchWindow view, bool asAdmin)
    {
        if (result.InstantResultOnExecuteFunc is { } callback && !asAdmin)
        {
            ShellThread.Run("ApplicationLaunch", () =>
            {
                try { if (callback()) OnUi(() => Hide(view)); }
                catch (Exception ex) { Report(result, ex); }
            });
            return true;
        }
        Hide(view);
        if (BuildParameterizedStartInfo(result, asAdmin) is { } parameterized && (asAdmin || result.InstantResultOnExecute == null))
        {
            ShellThread.Run("ApplicationLaunch", () =>
            {
                try { Process.Start(parameterized)?.Dispose(); }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
                catch (Exception ex) { Report(result, ex); }
            });
        }
        else if (asAdmin) FileExecutor.OpenFileOrFolderAsAdmin(string.IsNullOrWhiteSpace(result.InstantResultActionArgument)
            ? result.FullPath : result.InstantResultActionArgument);
        else if (result.InstantResultOnExecute is { } launch)
            ShellThread.Run("ApplicationLaunch", () => { try { launch(); } catch (Exception ex) { Report(result, ex); } });
        else FileExecutor.OpenFileOrFolder(result.FullPath);
        return true;
    }

    internal static ProcessStartInfo? BuildParameterizedStartInfo(AppSearchResult result, bool asAdmin)
        => string.IsNullOrEmpty(result.ApplicationArguments) && string.IsNullOrEmpty(result.ApplicationWorkingDirectory) ? null : new()
        {
            FileName = result.FullPath, Arguments = result.ApplicationArguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(result.ApplicationWorkingDirectory) ? Path.GetDirectoryName(result.FullPath) ?? "" : result.ApplicationWorkingDirectory,
            UseShellExecute = true, Verb = asAdmin ? "runas" : ""
        };

    private static void Hide(IPluginSearchWindow view)
    {
        (view as QuickSearchWindow)?.SuppressNextForegroundRestore();
        view.HideWindow();
    }
    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }
    private static void Report(AppSearchResult result, Exception ex)
    {
        Core.Logger.Log($"[ApplicationLaunch] Failed: {ex.Message}", Core.LogLevel.Error);
        OnUi(() => PluginSdk.Services.PluginNotificationService.Show(result.Name, ex.Message));
    }
}

