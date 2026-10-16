using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

/// <summary>
/// Handles downloading, extracting, installing, and dynamically loading Flow.Launcher community plugins.
/// </summary>
public static class FlowPluginInstaller
{
    private static readonly ConcurrentDictionary<string, bool> InstallingPlugins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly ConcurrentDictionary<string, string> InstallationErrors = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsInstalling(string pluginId) => !string.IsNullOrWhiteSpace(pluginId) && InstallingPlugins.ContainsKey(pluginId);
    internal static string? GetInstallationError(string pluginId) => string.IsNullOrWhiteSpace(pluginId) ? null : InstallationErrors.GetValueOrDefault(pluginId);

    public static async Task<bool> DownloadAndInstallPluginAsync(
        FlowCommunityPlugin plugin,
        FlowPluginHost host,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(plugin.ID) || string.IsNullOrWhiteSpace(plugin.UrlDownload))
            return false;

        if (!InstallingPlugins.TryAdd(plugin.ID, true)) return false;
        InstallationErrors.TryRemove(plugin.ID, out _);
        SearchRefreshService.RefreshIfMatches(q => true);

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"flow-package-{Guid.NewGuid():N}.zip");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"flow-extract-{Guid.NewGuid():N}");

        try
        {
            if (host.IsLoading(plugin.ID)) return false;
            if (!Uri.TryCreate(plugin.UrlDownload, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Plugin downloads require HTTPS.");
            var incompatibility = FlowPluginCompatibility.GetBlockReason(plugin.Language, plugin.MinimumAppVersion);
            if (incompatibility != null) throw new InvalidDataException(incompatibility);
            FlowPluginPathHelper.GetChildDirectory(GetPluginsDirectory(), plugin.Name);
            using var response = await HttpClient.GetAsync(plugin.UrlDownload, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs, token).ConfigureAwait(false);
            }

            if (FlowCommunityRecommendations.GetArchiveHash(plugin) is { } expectedHash)
            {
                using var archive = File.OpenRead(tempZipPath);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(archive), Convert.FromHexString(expectedHash)))
                    throw new InvalidDataException("Plugin package differs from the tested release.");
            }
            ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir, true);

            var installed = await InstallExtractedPluginAsync(plugin, ResolvePluginFolder(tempExtractDir), host, token).ConfigureAwait(false);
            if (!installed) InstallationErrors.TryAdd(plugin.ID, TranslationService.Get("FlowLauncherBridge_InstallFailed"));
            return installed;
        }
        catch (Exception ex)
        {
            InstallationErrors[plugin.ID] = $"{TranslationService.Get("FlowLauncherBridge_InstallFailed")}: {ex.Message}";
            PluginSdk.Logger.Log($"Flow plugin installation failed ({plugin.ID}): {ex}", PluginSdk.LogLevel.Error);
            return false;
        }
        finally
        {
            InstallingPlugins.TryRemove(plugin.ID, out _);
            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
            SafeDeleteDirectory(tempExtractDir);
            SearchRefreshService.RefreshIfMatches(q => true);
        }
    }

    private static string ResolvePluginFolder(string extractDir)
    {
        FlowPluginPathHelper.ValidateTree(extractDir);
        var manifests = Directory.GetFiles(extractDir, "plugin.json", SearchOption.AllDirectories);
        if (manifests.Length != 1) throw new InvalidDataException("Package must contain exactly one plugin manifest.");
        return Path.GetDirectoryName(manifests[0])!;
    }

    private static string GetPluginsDirectory() => Path.Combine(UserDataService.GetUserDataDirectory()
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro"), "FlowData", "Plugins");

    // Keep the previous directory intact until the replacement has initialized. Versioned
    // directories also avoid overwriting assemblies still mapped by Windows after unloading.
    internal static async Task<bool> InstallExtractedPluginAsync(FlowCommunityPlugin plugin, string source,
        FlowPluginHost host, CancellationToken token = default)
    {
        var root = GetPluginsDirectory();
        FlowPluginPathHelper.GetChildDirectory(root, plugin.Name);
        FlowPluginPathHelper.ValidateTree(source);
        var manifest = JsonSerializer.Deserialize<PluginMetadata>(await File.ReadAllTextAsync(Path.Combine(source, "plugin.json"), token),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest == null || !string.Equals(manifest.ID, plugin.ID, StringComparison.OrdinalIgnoreCase)) return false;
        FlowPluginPathHelper.GetChildDirectory(root, manifest.Name);
        if (FlowPluginCompatibility.GetBlockReason(manifest.Language, manifest.MinimumAppVersion) != null ||
            File.Exists(Path.Combine(source, ".deleted")) || File.Exists(Path.Combine(source, ".pending"))) return false;
        var existing = host.GetAllPlugins().FirstOrDefault(p => string.Equals(p.Metadata.ID, plugin.ID, StringComparison.OrdinalIgnoreCase));
        var oldDir = existing?.Metadata.PluginDirectory;
        if (!string.IsNullOrEmpty(oldDir)) FlowPluginPathHelper.ValidateChildDirectory(root, oldDir);
        var target = FlowPluginPathHelper.GetChildDirectory(root, $"{plugin.Name}-{Guid.NewGuid():N}");
        var unloaded = false;
        var installed = false;
        try
        {
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, ".pending"), "installing", token);
            CopyDirectory(source, target);
            token.ThrowIfCancellationRequested();
            if (existing?.Plugin is ISavable savable) savable.Save();
            await host.UnloadPluginAsync(plugin.ID).ConfigureAwait(false);
            unloaded = true;
            if (await host.LoadPluginFromDirectoryAsync(target, Path.Combine(target, "plugin.json")).ConfigureAwait(false))
            {
                File.Delete(Path.Combine(target, ".pending"));
                installed = true;
            }
            if (installed && !string.IsNullOrEmpty(oldDir)) SafeDeleteDirectory(oldDir);
            return installed;
        }
        finally
        {
            if (!installed)
            {
                if (host.FailedPlugins.TryGetValue(plugin.ID, out var failed))
                    InstallationErrors[plugin.ID] = $"{TranslationService.Get("FlowLauncherBridge_InstallFailed")}: {failed.Reason}";
                if (unloaded) await host.UnloadPluginAsync(plugin.ID).ConfigureAwait(false);
                SafeDeleteDirectory(target);
                if (unloaded && string.IsNullOrEmpty(oldDir)) host.ClearPluginFailure(plugin.ID);
                if (unloaded && !string.IsNullOrEmpty(oldDir))
                    await host.LoadPluginFromDirectoryAsync(oldDir, Path.Combine(oldDir, "plugin.json")).ConfigureAwait(false);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            var destSub = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destSub);
        }
    }

    public static async Task<bool> UninstallPluginAsync(PluginMetadata metadata, FlowPluginHost host)
    {
        if (string.IsNullOrWhiteSpace(metadata.ID) || !InstallingPlugins.TryAdd(metadata.ID, true)) return false;
        try
        {
            if (host.IsLoading(metadata.ID)) return false;
            var pluginsBaseDir = GetPluginsDirectory();
            var registered = host.GetAllPlugins().FirstOrDefault(p => string.Equals(p.Metadata.ID, metadata.ID, StringComparison.OrdinalIgnoreCase))?.Metadata;
            if (registered == null && host.FailedPlugins.TryGetValue(metadata.ID, out var failed)) registered = failed.Metadata;
            if (registered == null || string.IsNullOrEmpty(registered.PluginDirectory)) return false;
            var targetDir = registered.PluginDirectory;
            if (!string.IsNullOrEmpty(metadata.PluginDirectory) &&
                !string.Equals(Path.GetFullPath(metadata.PluginDirectory), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)) return false;
            FlowPluginPathHelper.ValidateChildDirectory(pluginsBaseDir, targetDir);
            if (Directory.Exists(targetDir)) FlowPluginPathHelper.ValidateTree(targetDir);
            await host.UnloadPluginAsync(metadata.ID).ConfigureAwait(false);
            SafeDeleteDirectory(targetDir);
            if (Directory.Exists(targetDir) && !File.Exists(Path.Combine(targetDir, ".deleted"))) return false;
            host.ClearPluginFailure(metadata.ID);
            SearchRefreshService.RefreshIfMatches(q => true);
            return true;
        }
        catch
        {
            return false;
        }
        finally { InstallingPlugins.TryRemove(metadata.ID, out _); }
    }

    internal static async Task<bool> RetryPluginAsync(PluginMetadata metadata, FlowPluginHost host)
    {
        if (string.IsNullOrWhiteSpace(metadata.ID) || !InstallingPlugins.TryAdd(metadata.ID, true)) return false;
        try
        {
            return await host.LoadPluginFromDirectoryAsync(metadata.PluginDirectory, Path.Combine(metadata.PluginDirectory, "plugin.json")).ConfigureAwait(false);
        }
        finally
        {
            InstallingPlugins.TryRemove(metadata.ID, out _);
            SearchRefreshService.RefreshIfMatches(q => true);
        }
    }

    public static void SafeDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;
        try
        {
            FlowPluginPathHelper.ValidateChildDirectory(Path.GetDirectoryName(Path.GetFullPath(directory))!, directory);
            FlowPluginPathHelper.ValidateTree(directory);
        }
        catch { return; }
        try
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch { }
            }
            Directory.Delete(directory, true);
        }
        catch
        {
            try
            {
                var marker = Path.Combine(directory, ".deleted");
                if (!File.Exists(marker))
                {
                    File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
                }
            }
            catch { }
        }
    }
}
