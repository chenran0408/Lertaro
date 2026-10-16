using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Downloads and provisions official embeddable Node.js portable packages for Flow JavaScript/TypeScript plugins.
/// Flattens the archive so node.exe resides directly in UserData\FlowData\NodeEmbeded-{arch}\node.exe.
/// </summary>
public static class FlowNodeDownloader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static async Task<string?> DownloadAndSetupEmbeddedNodeAsync(string targetDir)
    {
        try { FlowPluginPathHelper.ValidateChildDirectory(Path.GetDirectoryName(Path.GetFullPath(targetDir))!, targetDir); }
        catch { return null; }
        if (Directory.Exists(targetDir))
        {
            var existingExe = FindNodeInDir(targetDir);
            if (existingExe != null)
            {
                return existingExe;
            }
        }

        var url = GetDownloadUrl();
        var tempZip = Path.Combine(Path.GetTempPath(), $"node_embed_{Guid.NewGuid():N}.zip");
        var tempExtractDir = targetDir + $"-pending-{Guid.NewGuid():N}";

        try
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            FlowRuntimeIntegrity.VerifyArchive(tempZip, FlowRuntimeIntegrity.NodeHash(RuntimeInformation.ProcessArchitecture == Architecture.Arm64));
            ZipFile.ExtractToDirectory(tempZip, tempExtractDir, overwriteFiles: true);
            var subDirs = Directory.GetDirectories(tempExtractDir);
            var sourceDir = (subDirs.Length == 1 && Directory.GetFiles(tempExtractDir).Length == 0)
                ? subDirs[0]
                : tempExtractDir;

            if (FindNodeInDir(sourceDir) == null) return null;
            if (Directory.Exists(targetDir)) Community.FlowPluginInstaller.SafeDeleteDirectory(targetDir);
            Directory.Move(sourceDir, targetDir);

            return FindNodeInDir(targetDir);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
        }
    }

    public static string GetDownloadUrl()
    {
        var archSuffix = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "win-arm64"
            : "win-x64";

        return $"https://nodejs.org/dist/v{FlowRuntimeIntegrity.NodeVersion}/node-v{FlowRuntimeIntegrity.NodeVersion}-{archSuffix}.zip";
    }

    public static string? FindNodeInDir(string dir)
    {
        if (!Directory.Exists(dir))
            return null;

        var rootExe = Path.Combine(dir, "node.exe");
        if (File.Exists(rootExe))
            return rootExe;

        return null;
    }

}
