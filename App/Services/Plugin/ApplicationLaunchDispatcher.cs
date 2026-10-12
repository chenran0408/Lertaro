using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Services.Plugin;

// Application providers use ShellExecute, which can wait indefinitely on a shell extension or DDE.
// Keep their callbacks on the existing STA workers, and marshal only view changes back to the UI.
internal static class ApplicationLaunchDispatcher
{
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
        if (asAdmin) FileExecutor.OpenFileOrFolderAsAdmin(string.IsNullOrWhiteSpace(result.InstantResultActionArgument)
            ? result.FullPath : result.InstantResultActionArgument);
        else if (result.InstantResultOnExecute is { } launch)
            ShellThread.Run("ApplicationLaunch", () => { try { launch(); } catch (Exception ex) { Report(result, ex); } });
        else FileExecutor.OpenFileOrFolder(result.FullPath);
        return true;
    }

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

