using System.IO;
using System.Text.RegularExpressions;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Resolves the per-plugin data directories expected by Flow.Launcher plugins.
/// </summary>
internal static class FlowPluginPathHelper
{
    public static string GetSettingsDirectory(string userDataDirectory, string pluginName) =>
        GetChildDirectory(Path.Combine(userDataDirectory, "FlowData", "Settings", "Plugins"), pluginName);

    public static string GetCacheDirectory(string userDataDirectory, string pluginName) =>
        GetChildDirectory(Path.Combine(userDataDirectory, "FlowData", "Caches", "Plugins"), pluginName);

    internal static string GetChildDirectory(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' ') ||
            Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("Invalid plugin directory name.");
        var path = Path.GetFullPath(Path.Combine(root, name));
        ValidateChildDirectory(root, path);
        return path;
    }

    internal static void ValidateChildDirectory(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(fullPath), fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plugin directory is outside its workspace.");
        for (var dir = new DirectoryInfo(fullPath); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked plugin directories are not supported.");
    }

    internal static void ValidateTree(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked plugin directories are not supported.");
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked plugin files are not supported.");
            if (entry is DirectoryInfo child) ValidateTree(child.FullName);
        }
    }

    public static void ApplyTo(PluginMetadata metadata)
    {
        var userDataDirectory = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        metadata.PluginSettingsDirectoryPath = GetSettingsDirectory(userDataDirectory, metadata.Name);
        metadata.PluginCacheDirectoryPath = GetCacheDirectory(userDataDirectory, metadata.Name);
        Directory.CreateDirectory(metadata.PluginSettingsDirectoryPath);
        Directory.CreateDirectory(metadata.PluginCacheDirectoryPath);
    }
}
