using System.IO;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// The write half of <see cref="ClipboardStore"/>: capture inserts with dedup promotion, deletion,
/// clearing and the unpinned cap / retention trim. Split from the store purely for the per-file
/// line limit; holds no state -- everything is driven through the connection the store opens.
/// </summary>
internal static class ClipboardStoreWriter
{
    public static ClipboardEntry? Add(ClipboardDatabase database, ClipboardEntryDraft draft,
        string sourceProcess, string sourceWindowTitle, int maxEntries, int maxTextLength)
    {
        maxEntries = Math.Max(0, maxEntries);
        var content = draft.Kind == ClipboardEntryKind.Text
            ? ClipboardStoreSupport.TruncateText(draft.Content, maxTextLength)
            : ClipboardStoreSupport.BuildContent(draft);
        if (draft.Kind == ClipboardEntryKind.Text && content.Length == 0) return null;
        if (draft.Kind == ClipboardEntryKind.Image && string.IsNullOrEmpty(draft.ImagePath)) return null;

        var preview = ClipboardStoreSupport.BuildPreview(draft with { Content = content });
        var hash = draft.ComputeHash();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();

        // One monotonic stamp shared by the whole transaction: one past the newest stamp in the
        // table, never below the current clock. Stamping from the touched row's own value (or the
        // plain clock) would fail to lift an old row above rows repeatedly re-copied after it, and
        // two copies in the same millisecond would tie instead of ordering.
        var maxStamp = ClipboardDatabase.GetMaxStamp(connection, transaction);
        var stamp = Math.Max(now, maxStamp + 1);

        var existing = FindByHash(connection, transaction, hash);
        ClipboardEntry entry;
        if (existing != null)
        {
            entry = Promote(connection, transaction, existing, stamp);
        }
        else
        {
            entry = Insert(connection, transaction, draft, content, preview, hash,
                sourceProcess, sourceWindowTitle, stamp);
        }

        var trimmedImages = TrimUnpinned(connection, transaction, maxEntries, 0);
        transaction.Commit();
        UnlinkImages(database.ImagesDirectory, trimmedImages);

        // With MaxEntries 0 a new unpinned row is trimmed away in the same transaction; only report
        // entries the store actually keeps. An inserted or promoted row is the table-wide newest,
        // so any positive cap always keeps it.
        return entry.Pinned || maxEntries > 0 ? entry : null;
    }

    public static void Delete(ClipboardDatabase database, long id)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var images = DeleteWhere(connection, transaction, "id = @id", ("@id", id));
        transaction.Commit();
        UnlinkImages(database.ImagesDirectory, images);
    }

    public static void Clear(ClipboardDatabase database)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var images = DeleteWhere(connection, transaction, "1 = 1");
        transaction.Commit();
        UnlinkImages(database.ImagesDirectory, images);
    }

    public static void Trim(ClipboardDatabase database, int maxEntries, int retentionDays)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var images = TrimUnpinned(connection, transaction, Math.Max(0, maxEntries), Math.Max(0, retentionDays));
        transaction.Commit();
        UnlinkImages(database.ImagesDirectory, images);
    }

    private static ClipboardEntry? FindByHash(SqliteConnection connection, SqliteTransaction transaction, byte[] hash)
    {
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText =
            $"SELECT {ClipboardEntryColumns.EntryColumns} FROM clipboard_history WHERE content_sha256 = @hash ORDER BY id LIMIT 1";
        select.Parameters.AddWithValue("@hash", hash);
        using var reader = select.ExecuteReader();
        return reader.Read() ? ClipboardEntryColumns.Map(reader) : null;
    }

    private static ClipboardEntry Promote(SqliteConnection connection, SqliteTransaction transaction,
        ClipboardEntry existing, long stamp)
    {
        // Promotion updates the stamp and count but keeps the FIRST capture's source: "where did
        // this come from" should answer the origin, not whoever re-copied it most recently.
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE clipboard_history
            SET seen_count = seen_count + 1, last_seen_at_ms = @stamp
            WHERE id = @id
            """;
        update.Parameters.AddWithValue("@stamp", stamp);
        update.Parameters.AddWithValue("@id", existing.Id);
        update.ExecuteNonQuery();
        return existing with
        {
            SeenCount = existing.SeenCount + 1,
            LastSeenAtMs = stamp,
        };
    }

    private static ClipboardEntry Insert(SqliteConnection connection, SqliteTransaction transaction,
        ClipboardEntryDraft draft, string content, string preview, byte[] hash,
        string sourceProcess, string sourceWindowTitle, long stamp)
    {
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO clipboard_history
                (content, plain_preview, content_sha256, source_process, source_window_title, pinned, seen_count, created_at_ms, last_seen_at_ms, kind, image_path, image_width, image_height, html)
            VALUES
                (@content, @preview, @hash, @sourceProcess, @sourceWindowTitle, 0, 1, @stamp, @stamp, @kind, @imagePath, @imageWidth, @imageHeight, @html)
            RETURNING id
            """;
        insert.Parameters.AddWithValue("@content", content);
        insert.Parameters.AddWithValue("@preview", preview);
        insert.Parameters.AddWithValue("@hash", hash);
        insert.Parameters.AddWithValue("@sourceProcess", sourceProcess);
        insert.Parameters.AddWithValue("@sourceWindowTitle", sourceWindowTitle);
        insert.Parameters.AddWithValue("@stamp", stamp);
        insert.Parameters.AddWithValue("@kind", (int)draft.Kind);
        insert.Parameters.AddWithValue("@imagePath", draft.ImagePath);
        insert.Parameters.AddWithValue("@imageWidth", draft.ImageWidth);
        insert.Parameters.AddWithValue("@imageHeight", draft.ImageHeight);
        insert.Parameters.AddWithValue("@html", draft.Html);
        var id = (long)insert.ExecuteScalar()!;
        return new ClipboardEntry(id, content, preview, sourceProcess, sourceWindowTitle, false, 1, stamp, stamp,
            draft.Kind, draft.ImagePath, draft.ImageWidth, draft.ImageHeight, draft.Html);
    }

    /// <summary>Deletes unpinned rows beyond the cap and, when <paramref name="retentionDays"/> is
    /// positive, unpinned rows not seen within the window. Returns the image file names of the
    /// deleted rows so the caller can unlink them after the transaction commits.</summary>
    private static List<string> TrimUnpinned(SqliteConnection connection, SqliteTransaction transaction,
        int maxEntries, int retentionDays)
    {
        var cutoff = retentionDays > 0
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - retentionDays * 86_400_000L
            : long.MinValue;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Pinned entries are exempt from both the cap and the retention window: the cap applies to
        // the unpinned rows only, of which the newest by (LastSeenAtMs, Id) are kept. Tie-breaking
        // by Id is total, so two rows that share a millisecond never both fall outside the cap
        // while an older one survives.
        command.CommandText = """
            DELETE FROM clipboard_history
            WHERE pinned = 0 AND (
                last_seen_at_ms < @cutoff OR id NOT IN (
                    SELECT id FROM clipboard_history
                    WHERE pinned = 0
                    ORDER BY last_seen_at_ms DESC, id DESC
                    LIMIT @maxEntries
                )
            )
            RETURNING image_path
            """;
        command.Parameters.AddWithValue("@cutoff", cutoff);
        command.Parameters.AddWithValue("@maxEntries", maxEntries);

        return ReadImagePaths(command);
    }

    /// <summary>Deletes matching rows and returns their image file names. The caller commits before
    /// unlinking: a crash between commit and unlink leaves an orphan file (cleaned by a later
    /// sweep) but never a row pointing at a missing file.</summary>
    private static List<string> DeleteWhere(SqliteConnection connection, SqliteTransaction transaction,
        string where, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM clipboard_history WHERE {where} RETURNING image_path";
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        return ReadImagePaths(command);
    }

    private static List<string> ReadImagePaths(SqliteCommand command)
    {
        var images = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            if (path.Length > 0) images.Add(path);
        }
        return images;
    }

    private static void UnlinkImages(string imagesDirectory, List<string> fileNames)
    {
        foreach (var fileName in fileNames)
        {
            try { File.Delete(Path.Combine(imagesDirectory, fileName)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Orphaned pixels are cosmetic; the row is already gone. Nothing worth failing for.
            }
        }
    }
}
