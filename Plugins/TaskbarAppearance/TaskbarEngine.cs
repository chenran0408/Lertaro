using System.Diagnostics;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.TaskbarAppearance;

internal static class TaskbarEngine
{
    internal const string PackageFamily = "28017CharlesMilette.TranslucentTB_v826wp6bftszj";
    private static string PackageRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", PackageFamily);

    internal static string ResolveConfigPath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            if (!Directory.Exists(PackageRoot)) throw new InvalidOperationException(TranslationService.Get("Taskbar_NotInstalled"));
            return Path.Combine(PackageRoot, "RoamingState", "settings.json");
        }
        if (!Path.IsPathFullyQualified(executablePath) ||
            !Path.GetFileName(executablePath).Equals("TranslucentTB.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executablePath))
            throw new ArgumentException(TranslationService.Get("Taskbar_InvalidExecutable"));
        return Path.Combine(Path.GetDirectoryName(executablePath)!, "settings.json");
    }

    public static void Apply(TaskbarOptions options)
    {
        if (!options.Enabled && string.IsNullOrWhiteSpace(options.ExecutablePath) && !Directory.Exists(PackageRoot)) return;
        var path = ResolveConfigPath(options.ExecutablePath);
        var running = Process.GetProcessesByName("TranslucentTB");
        try
        {
            // Validate the selected engine before writing its settings. Do not kill another version
            // or start a second engine to force a refresh; the running engine watches settings.json.
            if (options.Enabled && running.Length > 0)
            {
                var expectedDirectory = string.IsNullOrWhiteSpace(options.ExecutablePath) ? null : Path.GetDirectoryName(options.ExecutablePath);
                if (running.Any(process => !MatchesEngine(process.MainModule?.FileName, expectedDirectory)))
                    throw new InvalidOperationException(TranslationService.Get("Taskbar_EngineConflict"));
            }
            if (!TaskbarConfigStore.Apply(path, options) || !options.Enabled || running.Length > 0) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(options.ExecutablePath)
                    ? $"shell:AppsFolder\\{PackageFamily}!TranslucentTB" : options.ExecutablePath,
                UseShellExecute = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(options.ExecutablePath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : Path.GetDirectoryName(options.ExecutablePath)!
            })?.Dispose();
        }
        finally { foreach (var process in running) process.Dispose(); }
    }

    internal static bool MatchesEngine(string? runningPath, string? portableDirectory)
        => runningPath != null && (portableDirectory == null
            ? runningPath.Contains("28017CharlesMilette.TranslucentTB_", StringComparison.OrdinalIgnoreCase)
            : string.Equals(Path.GetDirectoryName(runningPath), portableDirectory, StringComparison.OrdinalIgnoreCase));
}
