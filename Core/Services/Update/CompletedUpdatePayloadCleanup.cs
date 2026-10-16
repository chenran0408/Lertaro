namespace Lertaro.Core.Services.Update;

/// <summary>Reclaims verified-package staging left by older updaters after a completed update.</summary>
public static class CompletedUpdatePayloadCleanup
{
    // The updater starts the service before it writes its verdict and releases update.lock.
    // Wait in the background so startup and the App relaunch are never held up by cleanup.
    public static void Schedule(string installDirectory) => _ = Task.Run(async () =>
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                Clean(installDirectory);
                return;
            }
            catch (IOException) when (attempt < 59)
            {
                await Task.Delay(500).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Log($"[UpdateCleanup] Could not inspect old update files: {ex.Message}", LogLevel.Warn);
                return;
            }
        }
    });

    internal static int Clean(string installDirectory)
    {
        using var lease = UpdateApplyRequestHandler.AcquireUpdateLock(installDirectory);
        if (UpdateResult.ReadIfPresent(installDirectory)?.Succeeded != true) return 0;

        var root = Path.Combine(installDirectory, UpdateApplyRequestHandler.PayloadStagingFolderName);
        if (!Directory.Exists(root) || IsLink(root)) return 0;

        var removed = 0;
        foreach (var path in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(path), "N", out _)) continue;
            try
            {
                if (ContainsLink(path)) continue;
                Directory.Delete(path, recursive: true);
                removed++;
            }
            catch (Exception ex)
            {
                Logger.Log($"[UpdateCleanup] Could not remove {Path.GetFileName(path)}: {ex.Message}", LogLevel.Warn);
            }
        }
        if (removed > 0) Logger.Log($"[UpdateCleanup] Removed {removed} completed update staging directories.");
        return removed;
    }

    private static bool ContainsLink(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            if (IsLink(directory)) return true;
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (IsLink(entry)) return true;
                if (Directory.Exists(entry)) pending.Push(entry);
            }
        }
        return false;
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
