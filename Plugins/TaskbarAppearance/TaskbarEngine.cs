using System.Diagnostics;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.TaskbarAppearance;

internal static class TaskbarEngine
{
    private static readonly SemaphoreSlim Gate = new(1);
    private static bool _resumeAllowed = true;

    internal static void RestorePreview(TaskbarOptions saved)
    {
        if (!string.IsNullOrWhiteSpace(saved.ExecutablePath))
        {
            // A bundled preview never edits an external engine. Restore its prior absence without launching it.
            BundledTaskbarEngine.Stop();
            return;
        }
        if (!saved.Enabled) BundledTaskbarEngine.Stop();
        else if (BundledTaskbarEngine.IsRunning)
        {
            saved.IsBundled = true;
            TaskbarConfigStore.Apply(Path.Combine(BundledTaskbarEngine.RuntimeDirectory, "settings.json"), saved);
        }
    }

    internal static async Task SuspendAsync()
    {
        await Gate.WaitAsync();
        try { _resumeAllowed = false; await BundledTaskbarEngine.StopAsync(); }
        finally { Gate.Release(); }
    }

    public static Task ApplyAsync(TaskbarOptions options) => ApplyCoreAsync(() => options, resume: false);
    internal static Task ResumeAsync(Func<TaskbarOptions> readSaved) => ApplyCoreAsync(readSaved, resume: true);

    private static async Task ApplyCoreAsync(Func<TaskbarOptions> readOptions, bool resume)
    {
        await Gate.WaitAsync();
        try
        {
            if (resume && !_resumeAllowed) return;
            var options = readOptions();
            if (resume && (!options.Enabled || !string.IsNullOrWhiteSpace(options.ExecutablePath))) return;
            if (!resume) _resumeAllowed = true;
            // Reject invalid colors/rules before preparing frameworks, copying binaries or stopping a component.
            TaskbarConfiguration.Build("", options);
            if (BundledTaskbarEngine.IsStopping) await BundledTaskbarEngine.StopAsync();
            if (!string.IsNullOrWhiteSpace(options.ExecutablePath))
            {
                ResolveConfigPath(options.ExecutablePath);
                await BundledTaskbarEngine.StopAsync();
                await Task.Run(() => Apply(options));
                return;
            }
            options.IsBundled = true;
            var config = Path.Combine(BundledTaskbarEngine.RuntimeDirectory, "settings.json");
            if (!options.Enabled)
            {
                await Task.Run(() => TaskbarConfigStore.Apply(config, options));
                await BundledTaskbarEngine.StopAsync();
                return;
            }
            var running = Process.GetProcessesByName("TranslucentTB");
            try
            {
                if (running.Length > 0 && (!BundledTaskbarEngine.IsRunning || running.Any(p => !MatchesEngine(p.MainModule?.FileName, BundledTaskbarEngine.RuntimeDirectory))))
                    throw new InvalidOperationException(TranslationService.Get("Taskbar_EngineConflict"));
            }
            finally { foreach (var process in running) process.Dispose(); }
            var executable = await BundledTaskbarEngine.PrepareAsync();
            await Task.Run(() => TaskbarConfigStore.Apply(config, options));
            await BundledTaskbarEngine.StartAsync(executable);
        }
        finally { Gate.Release(); }
    }
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
