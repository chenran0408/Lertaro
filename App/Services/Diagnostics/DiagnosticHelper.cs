using System.IO;
using System.IO.Compression;
using Lertaro.Core;
using Lertaro.PluginSdk.Services;
using Microsoft.Win32;

namespace Lertaro.App.Services.Diagnostics;

/// <summary>
/// W3 three-process observability: (a) WER LocalDumps registration for App + Service processes
/// (HKLM when elevated, HKCU fallback -- WER reads both), (b) open-logs-folder, (c) diagnostic zip
/// (app.log + hook.log + glass-diagnostics + version info + dumps manifest). Zero network egress:
/// the zip is written locally and nothing is uploaded.
/// </summary>
internal static class DiagnosticHelper
{
    private static string UserDataDir =>
        UserDataService.GetUserDataDirectory() is { Length: > 0 } dir ? dir : "";

    private static string LogsDir => Path.Combine(UserDataDir, "logs");
    private static string DumpsDir => Path.Combine(UserDataDir, "dumps");

    /// <summary>(a) Registers WER LocalDumps for the App and Service processes. Tries HKLM first
    /// (system-wide, needs admin); falls back to HKCU (per-user, WER reads both). Idempotent --
    /// safe to call on every startup.</summary>
    public static void RegisterLocalDumps()
    {
        foreach (var processName in new[] { "Lertaro.App", "Lertaro.Service" })
        {
            if (!TryWriteLocalDumps(Registry.LocalMachine, processName) &&
                !TryWriteLocalDumps(Registry.CurrentUser, processName))
            {
                Logger.Log($"[Diagnostics] Could not register LocalDumps for {processName} (no registry access).", LogLevel.Warn);
            }
        }
    }

    private static bool TryWriteLocalDumps(RegistryKey hive, string processName)
    {
        try
        {
            using var key = hive.CreateSubKey($@"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\{processName}");
            if (key == null) return false;
            Directory.CreateDirectory(DumpsDir);
            key.SetValue("DumpFolder", DumpsDir, RegistryValueKind.ExpandString);
            key.SetValue("DumpCount", 5, RegistryValueKind.DWord);
            key.SetValue("DumpType", 1, RegistryValueKind.DWord); // 1 = mini dump
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>(b) Opens the log folder in Explorer (creates it if missing).</summary>
    public static void OpenLogsFolder()
    {
        Directory.CreateDirectory(LogsDir);
        System.Diagnostics.Process.Start("explorer.exe", LogsDir);
    }

    /// <summary>(c) Builds a diagnostic zip in the user's Documents folder. Contains app.log,
    /// hook.log, glass-diagnostics*.json, a version-info text file, and a dumps manifest. Returns
    /// the zip path, or null when nothing could be collected.</summary>
    public static string? BuildDiagnosticZip()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"lertaro-diag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var collected = 0;

            // Logs
            if (Directory.Exists(LogsDir))
            {
                var logStaging = Path.Combine(staging, "logs");
                Directory.CreateDirectory(logStaging);
                foreach (var file in Directory.GetFiles(LogsDir))
                {
                    if (File.Exists(file)) { File.Copy(file, Path.Combine(logStaging, Path.GetFileName(file))); collected++; }
                }
            }

            // Dumps manifest (file names + sizes only, not the dumps themselves -- they can be large)
            if (Directory.Exists(DumpsDir))
            {
                var dumpFiles = Directory.GetFiles(DumpsDir);
                if (dumpFiles.Length > 0)
                {
                    var manifest = Path.Combine(staging, "dumps-manifest.txt");
                    File.WriteAllLines(manifest,
                        dumpFiles.Select(f => $"{Path.GetFileName(f)}  {new FileInfo(f).Length} bytes"));
                    collected++;
                }
            }

            // Version info
            var versionInfo = Path.Combine(staging, "version.txt");
            var app = System.Reflection.Assembly.GetEntryAssembly();
            var version = app?.GetName().Version?.ToString() ?? "unknown";
            File.WriteAllLines(versionInfo,
            [
                $"Lertaro {version}",
                $"OS: {Environment.OSVersion}",
                $"64-bit: {Environment.Is64BitProcess}",
                $"Machine: {Environment.MachineName}",
                $"User: {Environment.UserName}",
                $"CLR: {Environment.Version}",
                $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            ]);
            collected++;

            if (collected == 0) return null;

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var zipPath = Path.Combine(documents, $"lertaro-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(staging, zipPath);
            return zipPath;
        }
        catch (Exception ex)
        {
            Logger.Log($"[Diagnostics] Failed to build diagnostic zip: {ex.Message}", LogLevel.Error);
            return null;
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }
    }
}
