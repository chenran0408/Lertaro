namespace Lertaro.Core;

internal static class FileRecordStoreReplaceHelper
{
    public static void ReplaceWithRetry(string tempPath, string finalPath, Action<string> tryDelete,
        Action? beforeBackupDelete = null, string? backupPath = null)
    {
        const int maxAttempts = 5;
        backupPath ??= finalPath + ".bak";
        var replacedExisting = false;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(finalPath))
                {
                    File.Replace(tempPath, finalPath, backupPath, ignoreMetadataErrors: true);
                    replacedExisting = true;
                }
                else
                {
                    File.Move(tempPath, finalPath, overwrite: true);
                }
                break;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }

        // A failed callback is not a failed replacement: never retry a consumed temp file.
        beforeBackupDelete?.Invoke();
        if (replacedExisting)
            tryDelete(backupPath);
    }
}
