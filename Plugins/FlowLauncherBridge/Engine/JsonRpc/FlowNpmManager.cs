using System.Diagnostics;
using System.IO;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Manages automated npm dependency resolution (package.json) for Flow JavaScript/TypeScript plugins.
/// Executes npm via embedded node.exe and npm-cli.js to avoid console window flashing.
/// </summary>
public static class FlowNpmManager
{
    // Serializes npm installs: a concurrent Ensure while one is running must not delete the
    // node_modules being written or run a second npm over the same tree.
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    internal static Func<string, string, string, Task<bool>>? ProcessRunnerOverride { get; set; }

    public static void EnsureNpmAndPackagesBackground(string nodeExe, string pluginDir)
    {
        _ = Task.Run(async () =>
        {
            try { await EnsureNpmAndPackagesAsync(nodeExe, pluginDir).ConfigureAwait(false); }
            catch (Exception ex) { PluginSdk.Logger.Log($"Node dependency preparation failed: {ex.Message}", PluginSdk.LogLevel.Error); }
        });
    }

    public static async Task<bool> EnsureNpmAndPackagesAsync(string nodeExe, string pluginDir)
    {
        var packageJson = Path.Combine(pluginDir, "package.json");
        var markerFile = Path.Combine(pluginDir, ".npm_installed");
        var nodeModulesDir = Path.Combine(pluginDir, "node_modules");

        // The marker is the only "installed" signal. node_modules alone used to count as done too,
        // which permanently masked a half-install left behind when npm was killed mid-run: the
        // marker never got written, the directory existed, and every later load skipped the install.
        if (!File.Exists(packageJson)) return true;
        await InstallGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var fingerprint = FlowDependencyMarker.Fingerprint(nodeExe, packageJson, Path.Combine(pluginDir, "package-lock.json"));
            if (FlowDependencyMarker.Matches(markerFile, fingerprint)) return true;
            File.Delete(markerFile);
            FlowPluginPathHelper.ValidateTree(pluginDir);
            // Many published plugins already bundle their production dependencies. Validate
            // them before reinstalling; their development-only prepare scripts may not run in a release ZIP.
            if (Directory.Exists(nodeModulesDir) &&
                await RunNpmAsync(nodeExe, pluginDir, "ls --omit=dev --depth=0").ConfigureAwait(false))
            {
                await File.WriteAllTextAsync(markerFile, fingerprint).ConfigureAwait(false);
                return true;
            }
            // Leftover tree from a previous killed install: clear it so the reinstall starts
            // clean instead of npm merging into a possibly corrupt dependency tree.
            if (Directory.Exists(nodeModulesDir))
            {
                try { Directory.Delete(nodeModulesDir, recursive: true); }
                catch { /* best-effort; npm reinstalls over what it can */ }
            }
            return await InstallPackagesAsync(nodeExe, pluginDir, markerFile).ConfigureAwait(false);
        }
        finally
        {
            InstallGate.Release();
        }
    }

    public static async Task<bool> InstallPackagesAsync(string nodeExe, string pluginDir, string markerFile)
    {
        try
        {
            var success = await RunNpmAsync(nodeExe, pluginDir, "install --omit=dev --no-audit --no-fund").ConfigureAwait(false);
            if (success)
            {
                // npm may create/update the lock file while resolving dependencies.
                var fingerprint = FlowDependencyMarker.Fingerprint(nodeExe, Path.Combine(pluginDir, "package.json"), Path.Combine(pluginDir, "package-lock.json"));
                await File.WriteAllTextAsync(markerFile, fingerprint).ConfigureAwait(false);
            }
            return success;
        }
        catch
        {
            return false;
        }
    }

    private static Task<bool> RunNpmAsync(string nodeExe, string pluginDir, string arguments)
    {
        var nodeDir = Path.GetDirectoryName(nodeExe) ?? string.Empty;
        var npmCliJs = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");
        // Official embedded Node includes npm-cli.js; invoke it directly without a command shell.
        return File.Exists(npmCliJs)
            ? RunProcessAsync(nodeExe, $"\"{npmCliJs}\" {arguments}", pluginDir)
            : Task.FromResult(false);
    }

    private static async Task<bool> RunProcessAsync(string exe, string arguments, string workingDir)
    {
        if (ProcessRunnerOverride != null) return await ProcessRunnerOverride(exe, arguments, workingDir).ConfigureAwait(false);
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
            process.StandardInput.Close();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            // Drain both pipes WHILE waiting: npm writes far more progress output than the ~4KB
            // pipe buffer holds, and a child blocked on a full stdout pipe never exits -- a
            // wait-only version deadlocked here until the timeout killed npm mid-install, which
            // is also what left half-written node_modules trees behind.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync(cts.Token);
            await exitTask.ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return false;
        }
    }
}
