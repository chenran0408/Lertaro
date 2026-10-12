using System.Diagnostics;
using System.IO;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

// Shared launch construction for registry entries and explicitly added portable programs.
internal static class ApplicationCatalogEntries
{
    internal static SearchableItem Create(string name, string path, string arguments = "", string directory = "", IReadOnlyList<string>? aliases = null) => new()
    {
        Title = name, Description = path, ResultKind = "Application", ActionType = "None", ActionArgument = path,
        LaunchArguments = arguments, LaunchWorkingDirectory = directory, Keywords = aliases ?? [],
        OnExecute = () =>
        {
            try { Process.Start(BuildStartInfo(path, arguments, directory))?.Dispose(); }
            catch (Exception ex) { PluginSdk.Logger.Log($"[ApplicationCatalog] Launch failed: {ex.Message}", PluginSdk.LogLevel.Error); }
        }
    };

    internal static ProcessStartInfo BuildStartInfo(string path, string arguments, string directory) => new()
    {
        FileName = path, Arguments = arguments, UseShellExecute = true,
        WorkingDirectory = string.IsNullOrWhiteSpace(directory) ? Path.GetDirectoryName(path) ?? "" : directory
    };
}
