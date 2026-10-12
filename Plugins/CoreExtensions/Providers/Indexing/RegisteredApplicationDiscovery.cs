using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

internal readonly record struct RegisteredApplication(string Name, string Path);

// Windows App Paths is a separate source: applications need not create a Start Menu shortcut.
internal static class RegisteredApplicationDiscovery
{
    private const string Root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";

    internal static List<RegisteredApplication> Read()
    {
        var entries = new List<RegisteredApplication>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 })
            {
                try
                {
                    using var registry = RegistryKey.OpenBaseKey(hive, view);
                    using var root = registry.OpenSubKey(Root);
                    if (root == null) continue;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var key = root.OpenSubKey(name);
                            if (NormalizePath(key?.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string) is { } path)
                                entries.Add(new(name, path));
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            }
        return entries.Distinct().ToList();
    }

    internal static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = Environment.ExpandEnvironmentVariables(value.Trim());
        if (path.StartsWith('"') && path.EndsWith('"')) path = path[1..^1];
        if (path.Contains('"') || !System.IO.Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        if (string.IsNullOrEmpty(System.IO.Path.GetExtension(path))) path += ".exe";
        return System.IO.Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    internal static List<SearchableItem> Collect(IEnumerable<RegisteredApplication> entries, Func<string, bool> exists, Func<string, string?> title)
        => entries.Where(entry => exists(entry.Path)).GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => ApplicationCatalogEntries.Create(title(group.Key)?.Trim() is { Length: > 0 } name ? name : System.IO.Path.GetFileNameWithoutExtension(group.Key), group.Key,
                aliases: group.Select(entry => System.IO.Path.GetFileNameWithoutExtension(entry.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())).ToList();

    internal static List<SearchableItem> GetItems(IEnumerable<RegisteredApplication>? entries = null) => Collect(entries ?? Read(), ExistsLocally, GetTitle);

    internal static bool ExistsLocally(string path)
    {
        try { return new DriveInfo(System.IO.Path.GetPathRoot(path)!).DriveType != DriveType.Network && File.Exists(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    internal static string RuntimeFingerprint(IEnumerable<RegisteredApplication> entries)
        => Fingerprint(entries.Select(entry => entry with { Name = entry.Name + (ExistsLocally(entry.Path) ? ":present" : ":missing") }));

    internal static string? GetTitle(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).FileDescription; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return null; }
    }

    internal static string Fingerprint(IEnumerable<RegisteredApplication> entries) => string.Join('\n', entries
        .Select(entry => entry.Name.ToUpperInvariant() + "\0" + entry.Path.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal));
}
