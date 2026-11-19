using System.IO;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ClipboardHistory;

// Zip-level backup surgery, kept free of WPF and of the store so both the runtime import path
// and tests can drive it: Export snapshots the live database with VACUUM INTO (a consistent,
// compact copy that never holds locks on the working file) and packs it with the image files;
// Extract validates a backup zip and stages its contents for a wholesale file swap.
internal static class ClipboardBackupSupport
{
    private const string DatabaseFileName = "clipboard-history.db";

    public static void Export(string databasePath, string imagesDirectory, string destinationZip)
    {
        var staging = Path.Combine(Path.GetTempPath(), $"lertaro-clip-export-{Guid.NewGuid():N}");
        var temporaryZip = $"{destinationZip}.{Guid.NewGuid():N}.partial";
        Directory.CreateDirectory(staging);
        try
        {
            using (var connection = OpenConnection(databasePath))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"VACUUM INTO '{Path.Combine(staging, DatabaseFileName).Replace("'", "''")}'";
                command.ExecuteNonQuery();
            }

            if (Directory.Exists(imagesDirectory))
            {
                Directory.CreateDirectory(Path.Combine(staging, "images"));
                foreach (var file in Directory.GetFiles(imagesDirectory))
                    File.Copy(file, Path.Combine(staging, "images", Path.GetFileName(file)));
            }

            ZipFile.CreateFromDirectory(staging, temporaryZip);
            File.Move(temporaryZip, destinationZip, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryZip);
            try { Directory.Delete(staging, recursive: true); }
            catch (IOException) { /* a staging leftover is cosmetic; the zip is already closed */ }
        }
    }

    /// <summary>Extracts a backup zip into a staging pair (database + images) after checking it
    /// actually contains a database. Throws on anything that is not a clipboard backup; the
    /// caller's live files are untouched until it stages successfully.</summary>
    public static (string DatabasePath, string ImagesDirectory) Extract(string zipPath, string stagingRoot)
    {
        Directory.CreateDirectory(stagingRoot);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, stagingRoot, overwriteFiles: true);
            var databasePath = Path.Combine(stagingRoot, DatabaseFileName);
            if (!File.Exists(databasePath))
                throw new InvalidDataException("Backup zip does not contain a clipboard database.");

            var images = Path.Combine(stagingRoot, "images");
            Directory.CreateDirectory(images);
            return (databasePath, images);
        }
        catch
        {
            Directory.Delete(stagingRoot, recursive: true);
            throw;
        }
    }

    internal static void ValidateDatabase(string path)
    {
        using var connection = OpenConnection(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'clipboard_history'";
        if (command.ExecuteScalar() is null)
            throw new InvalidDataException("Backup does not contain a clipboard history table.");

        command.CommandText = "PRAGMA quick_check(1)";
        if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("Backup clipboard database is damaged.");
    }

    internal static void ReplaceLiveFiles(string databasePath, string imagesDirectory,
        string stagedDatabase, string stagedImages)
    {
        var previous = Path.Combine(Path.GetDirectoryName(databasePath)!, $".previous-{Guid.NewGuid():N}");
        Directory.CreateDirectory(previous);
        var moves = new Stack<(string From, string To, bool IsDirectory)>();
        var preservePrevious = false;
        try
        {
            foreach (var suffix in new[] { "-journal", "-wal", "-shm", string.Empty })
            {
                var path = databasePath + suffix;
                if (File.Exists(path))
                    Move(path, Path.Combine(previous, Path.GetFileName(path)), isDirectory: false);
            }
            if (Directory.Exists(imagesDirectory))
                Move(imagesDirectory, Path.Combine(previous, "images"), isDirectory: true);
            Move(stagedDatabase, databasePath, isDirectory: false);
            Move(stagedImages, imagesDirectory, isDirectory: true);
        }
        catch
        {
            preservePrevious = true;
            foreach (var (from, to, isDirectory) in moves)
            {
                if (isDirectory) Directory.Move(to, from);
                else File.Move(to, from);
            }
            preservePrevious = false;
            throw;
        }
        finally
        {
            if (!preservePrevious)
                DeleteStagingDirectory(previous);
        }

        void Move(string from, string to, bool isDirectory)
        {
            if (isDirectory) Directory.Move(from, to);
            else File.Move(from, to);
            moves.Push((from, to, isDirectory));
        }
    }

    internal static void DeleteStagingDirectory(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static SqliteConnection OpenConnection(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly, // VACUUM INTO writes the copy, not the source
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }
}
