using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.TaskbarAppearance;

// Bundled binaries stay separate from the MIT host. Each user's writable runtime owns its settings.json.
internal static class BundledTaskbarEngine
{
    private static Process? _host;
    private static volatile bool _stopping;
    private static readonly SemaphoreSlim Gate = new(1);
    internal static string BundleDirectory => Path.Combine(AppContext.BaseDirectory, "Components", "TranslucentTB");
    internal static string RuntimeDirectory => Path.Combine(UserDataService.GetUserDataDirectory()
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro"),
        "Taskbar", "2026.2-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
    internal static bool IsRunning => _host is { HasExited: false };
    internal static bool IsStopping => _stopping;

    internal static async Task<string> PrepareAsync()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new InvalidOperationException(TranslationService.Get("Taskbar_Windows11Required"));
        var manifestPath = Path.Combine(BundleDirectory, "manifest.json");
        if (!File.Exists(manifestPath)) throw new FileNotFoundException(TranslationService.Get("Taskbar_BundleMissing"));
        await Gate.WaitAsync();
        try
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        if (!string.Equals(manifest.RootElement.GetProperty("architecture").GetString(), RuntimeInformation.ProcessArchitecture.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(TranslationService.Get("Taskbar_BundleMissing"));
            Directory.CreateDirectory(RuntimeDirectory);
            foreach (var file in manifest.RootElement.GetProperty("engineFiles").EnumerateArray())
            {
                var relative = file.GetProperty("path").GetString()!;
                var hash = file.GetProperty("sha256").GetString()!;
                var source = SafePath(Path.Combine(BundleDirectory, "engine"), relative);
                var target = SafePath(RuntimeDirectory, relative);
                if (File.Exists(target) && await HasHashAsync(target, hash)) continue;
                if (IsRunning) throw new IOException("Cannot replace a running taskbar component.");
                if (!await HasHashAsync(source, hash)) throw new InvalidDataException("Taskbar component checksum mismatch.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, true);
            }
            await EnsureFrameworksAsync();
            return Path.Combine(RuntimeDirectory, "TranslucentTB.exe");
        }
        finally { Gate.Release(); }
    }

    internal static string SafePath(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathFullyQualified(relative) || !path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid taskbar component path.");
        return path;
    }

    private static async Task<bool> HasHashAsync(string path, string expected)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EnsureFrameworksAsync()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(BundleDirectory, "EnsureDependencies.ps1"), "-DependencyDirectory", Path.Combine(BundleDirectory, "Dependencies"), "-Architecture", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant() })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not check taskbar dependencies.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await output;
        if (process.ExitCode != 0) throw new IOException(TranslationService.Get("Taskbar_DependenciesFailed") + "\n" + await error);
    }

    internal static async Task StartAsync(string executable)
    {
        if (IsRunning) return;
        var hostPath = Path.Combine(AppContext.BaseDirectory, "Lertaro.TaskbarHost.exe");
        if (!File.Exists(hostPath)) throw new FileNotFoundException(TranslationService.Get("Taskbar_BundleMissing"));
        var status = Path.Combine(RuntimeDirectory, "host-" + Guid.NewGuid().ToString("N") + ".status");
        using var parent = Process.GetCurrentProcess();
        var start = new ProcessStartInfo(hostPath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var argument in new[] { Environment.ProcessId.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), executable, status })
            start.ArgumentList.Add(argument);
        _host?.Dispose();
        _host = Process.Start(start) ?? throw new IOException("Could not start taskbar host.");
        try
        {
            for (var attempt = 0; attempt < 80; attempt++)
            {
                if (File.Exists(status))
                {
                    var result = await File.ReadAllTextAsync(status);
                    if (result == "ready") return;
                    if (result.StartsWith("error:", StringComparison.Ordinal)) throw new IOException(result[6..]);
                }
                if (_host.HasExited) throw new IOException("Taskbar host exited before startup completed.");
                await Task.Delay(100);
            }
            Stop();
            throw new TimeoutException("Taskbar component startup timed out.");
        }
        finally { if (File.Exists(status)) File.Delete(status); }
    }

    internal static void Stop()
    {
        _stopping = IsRunning;
        try { using var stop = EventWaitHandle.OpenExisting($"Local\\Lertaro.Taskbar.{Environment.ProcessId}"); stop.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    internal static async Task StopAsync()
    {
        Stop();
        if (_host is { HasExited: false } host)
        {
            await Task.WhenAny(host.WaitForExitAsync(), Task.Delay(12000));
            if (!host.HasExited) throw new IOException("Taskbar component is still closing.");
        }
        _stopping = false;
    }
}
