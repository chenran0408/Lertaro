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

            if (File.Exists(destinationZip)) File.Delete(destinationZip);
            ZipFile.CreateFromDirectory(staging, destinationZip);
        }
        finally
        {
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
        ZipFile.ExtractToDirectory(zipPath, stagingRoot, overwriteFiles: true);

        var databasePath = Path.Combine(stagingRoot, DatabaseFileName);
        if (!File.Exists(databasePath))
        {
            Directory.Delete(stagingRoot, recursive: true);
            throw new InvalidDataException("Backup zip does not contain a clipboard database.");
        }

        var images = Path.Combine(stagingRoot, "images");
        if (!Directory.Exists(images)) Directory.CreateDirectory(images);
        return (databasePath, images);
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
