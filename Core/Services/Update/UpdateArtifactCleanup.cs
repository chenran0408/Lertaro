
namespace Lertaro.Core.Services.Update;

/// <summary>
/// Best-effort cleanup of the artifacts the retired silent-update path left on users' machines:
/// the install-directory staging and verdict files, the relaunch marker in the shared data
/// directory, and the per-run staging directories in %TEMP%. Every delete is individually guarded --
/// a locked file, a missing folder or a service still holding the lock is not an error, it is just
/// a leftover that survives until the next run. Intended to run once per App start.
/// </summary>
/// <remarks>
/// This deliberately does <em>not</em> touch the pipe message or the service-side handler that the
/// old flow used: installs of 6.3.5 and earlier still carry that code, and the service-side half of
/// this install keeps answering them. Only the artifacts are garbage.
/// </remarks>
public static class UpdateArtifactCleanup
{
    /// <summary>Runs once per process. The second call is a no-op.</summary>
    private static int _ran;

    public static void Run()
    {
        if (Interlocked.Exchange(ref _ran, 1) != 0)
            return;

        try { CleanInstallDirectory(AppContext.BaseDirectory); }
        catch (Exception ex) { Logger.Log($"[UpdateCleanup] Install directory: {ex.Message}", LogLevel.Debug); }

        try { File.Delete(Path.Combine(Logger.SharedDataDir, "update-relaunch")); }
        catch (Exception ex) { Logger.Log($"[UpdateCleanup] Relaunch marker: {ex.Message}", LogLevel.Debug); }

        try
        {
            var temp = Path.GetTempPath();
            foreach (var dir in Directory.EnumerateDirectories(temp, "LertaroUpdate-*"))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex) { Logger.Log($"[UpdateCleanup] {dir}: {ex.Message}", LogLevel.Debug); }
            }
        }
        catch (Exception ex) { Logger.Log($"[UpdateCleanup] Temp sweep: {ex.Message}", LogLevel.Debug); }
    }

    /// <summary>The install directory's own leftovers. update.lock is skipped while a live service
    /// holds it -- File.Delete on an open file throws, which the guard swallows, and the next run
    /// picks it up.</summary>
    internal static void CleanInstallDirectory(string installDirectory)
    {
        var payloadRoot = Path.Combine(installDirectory, "update-payload");
        if (Directory.Exists(payloadRoot))
            Directory.Delete(payloadRoot, recursive: true);

        File.Delete(Path.Combine(installDirectory, "update-result.json"));
        File.Delete(Path.Combine(installDirectory, "update.lock"));
    }
}
