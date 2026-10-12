using System.IO;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;

/// <summary>
/// Dynamic action provider that loads the standard Windows Explorer context menu for files/folders.
/// </summary>
public class ShellMenuActionProvider : IDynamicActionProvider
{
    public string GroupName => TranslationService.Get("Plugin_ShellGroup");
    public string Description => TranslationService.Get("Plugin_ShellGroup_Desc");
    public int Priority => -1;

    private ShellMenuSession? _session;
    private string? _lastPath;

    // Build only when a supported file menu is requested. Warming a folder menu for an application
    // click loads unrelated third-party handlers into the process before CanProvide can reject it.
    public void Init() { }

    public bool CanProvide(IReadOnlyList<ISearchResult> results)
    {
        // The native shell menu is single-item only for now (multi-file menu needs multi-PIDL);
        // hide it when more than one result is selected.
        if (results.Count != 1) return false;
        var result = results[0];
        if (result == null || string.IsNullOrEmpty(result.FullPath) || result.IsApplication) return false;
        var extension = Path.GetExtension(result.FullPath);
        if (!result.IsDir && (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase))) return false;
        return PluginSdk.Helpers.PathExistenceCache.ExistsResult(result);
    }

    public IEnumerable<DynamicMenuItem> GetMenuItems(IReadOnlyList<ISearchResult> results, IntPtr hMenu)
    {
        var result = results[0];
        // If root menu, create a new session
        if (hMenu == IntPtr.Zero)
        {
            _session?.Dispose();
            _session = ShellMenuSession.Create(result.FullPath);
            _lastPath = result.FullPath;
        }

        if (_session == null)
        {
            return Array.Empty<DynamicMenuItem>();
        }

        try
        {
            var items = _session.EnumerateItems(hMenu);
            var menuItems = new List<DynamicMenuItem>();
            foreach (var item in items)
            {
                menuItems.Add(new DynamicMenuItem
                {
                    Text = item.Text,
                    CommandId = item.CommandId,
                    IsSeparator = item.IsSeparator,
                    HasSubMenu = item.HasSubMenu,
                    SubMenuHandle = item.SubMenuHandle,
                    IsDisabled = item.IsDisabled,
                    HBitmapItem = item.HBitmapItem
                });
            }
            return menuItems;
        }
        catch
        {
            return Array.Empty<DynamicMenuItem>();
        }
    }

    public void ExecuteCommand(IReadOnlyList<ISearchResult> results, uint commandId, IntPtr ownerHwnd)
    {
        var sessionToExecute = _session;
        _session = null; // Detach to allow parallel executions/cleanup

        if (sessionToExecute != null)
        {
            Task.Run(() =>
            {
                try
                {
                    sessionToExecute.InvokeCommand(commandId, ownerHwnd);
                }
                finally
                {
                    sessionToExecute.Dispose();
                }
            });
        }
    }

    public void ClearSession()
    {
        _session?.Dispose();
        _session = null;
        _lastPath = null;
    }
}
