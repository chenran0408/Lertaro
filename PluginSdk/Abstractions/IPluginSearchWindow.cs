namespace Lertaro.PluginSdk.Abstractions;

/// <summary>
/// Minimal search window interface exposed to plugins.
/// </summary>
public interface IPluginSearchWindow
{
    /// <summary>Locates the specified path in Windows Explorer.</summary>
    void LocateInExplorerExternal(string path);

    /// <summary>Opens the specified file or folder using default application.</summary>
    void OpenFileOrFolderExternal(string path);

    /// <summary>Opens the specified file or folder with administrator privileges.</summary>
    void OpenFileOrFolderAsAdminExternal(string path);

    /// <summary>Opens a result while allowing the host to preserve application launch options.</summary>
    void OpenSearchResultExternal(ISearchResult result, bool asAdmin = false)
    {
        if (asAdmin) OpenFileOrFolderAsAdminExternal(result.FullPath);
        else OpenFileOrFolderExternal(result.FullPath);
    }

    /// <summary>Closes or hides the search window.</summary>
    void HideWindow();
}
