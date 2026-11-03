using System.IO;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// SQLite-backed clipboard history storage for all entry kinds. Owns dedup (the same content
/// re-captured merges into its row and is promoted back to the top with a monotonically increasing
/// LastSeenAtMs), text truncation, the unpinned entry cap, the retention window and corruption
/// recovery on initialization. Image pixels live as content-addressed PNG files under
/// <paramref name="imagesDirectory"/>; the store keeps rows in sync with those files by unlinking
/// the PNG of every row it deletes.
/// </summary>
/// <remarks>
/// Microsoft.Data.Sqlite is synchronous, so every operation runs on the thread pool via Task.Run
/// while callers await; combined with the per-store SemaphoreSlim this keeps SQLite calls serialized
/// and never blocks a UI thread on disk I/O. Connections are opened per call and disposed: no
/// IDisposable on the store itself, and pooling is off so no pooled connection keeps the database
/// file open (corruption recovery must be able to rename it aside).
/// </remarks>
internal sealed class ClipboardStore(string databasePath, string imagesDirectory)
{
    // Column order shared by every SELECT; MapEntry depends on it.
    private const string EntryColumns =
        "id, content, plain_preview, source_process, source_window_title, pinned, seen_count, created_at_ms, last_seen_at_ms, kind, image_path, image_width, image_height";
    private const string SearchEntryColumns =
        "h.id, h.content, h.plain_preview, h.source_process, h.source_window_title, h.pinned, h.seen_count, h.created_at_ms, h.last_seen_at_ms, h.kind, h.image_path, h.image_width, h.image_height";

    private readonly ClipboardDatabase _database = new(databasePath);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync(InitializeCore, cancellationToken);

    public Task<ClipboardEntry?> AddAsync(ClipboardEntryDraft draft, string sourceProcess, string sourceWindowTitle,
        int maxEntries, int maxTextLength, CancellationToken cancellationToken = default) =>
        RunLockedAsync(
            () => AddCore(draft, sourceProcess ?? string.Empty, sourceWindowTitle ?? string.Empty,
                maxEntries, maxTextLength),
            cancellationToken);

    public Task<IReadOnlyList<ClipboardEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync<IReadOnlyList<ClipboardEntry>>(() => GetEntriesCore(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ClipboardEntry>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        RunLockedAsync<IReadOnlyList<ClipboardEntry>>(() => SearchCore(query, cancellationToken), cancellationToken);

    public Task SetPinnedAsync(long id, bool pinned, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => SetPinnedCore(id, pinned), cancellationToken);

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => DeleteCore(id), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync(ClearCore, cancellationToken);

    public Task TrimAsync(int maxEntries, int retentionDays, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => TrimCore(Math.Max(0, maxEntries), Math.Max(0, retentionDays)), cancellationToken);

    // The serialization shell shared by every public API: hold the gate, run the synchronous SQLite
    // work on the thread pool, always release. Split out purely to keep this file under the per-file
    // line limit; it holds no state of its own.
    private async Task RunLockedAsync(Action work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> RunLockedAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void InitializeCore()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        try
        {
            _database.EnsureSchema();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is ClipboardDatabase.SqliteCorrupt or ClipboardDatabase.SqliteNotADatabase)
        {
            _database.MoveCorruptDatabaseAside();
            _database.EnsureSchema();
        }
    }

    private SqliteConnection OpenConnection() => _database.OpenConnection();

    private ClipboardEntry? AddCore(ClipboardEntryDraft draft, string sourceProcess, string sourceWindowTitle,
        int maxEntries, int maxTextLength)
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

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        // One monotonic stamp shared by the whole transaction: one past the newest stamp in the
        // table, never below the current clock. Stamping from the touched row's own value (or the
        // plain clock) would fail to lift an old row above rows repeatedly re-copied after it, and
        // two copies in the same millisecond would tie instead of ordering.
        var maxStamp = ClipboardDatabase.GetMaxStamp(connection, transaction);
        var stamp = Math.Max(now, maxStamp + 1);

        ClipboardEntry? existing;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                $"SELECT {EntryColumns} FROM clipboard_history WHERE content_sha256 = @hash ORDER BY id LIMIT 1";
            select.Parameters.AddWithValue("@hash", hash);
            using var reader = select.ExecuteReader();
            existing = reader.Read() ? MapEntry(reader) : null;
        }

        ClipboardEntry entry;
        if (existing != null)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE clipboard_history
                SET seen_count = seen_count + 1, last_seen_at_ms = @stamp,
                    source_process = @sourceProcess, source_window_title = @sourceWindowTitle
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("@stamp", stamp);
            update.Parameters.AddWithValue("@sourceProcess", sourceProcess);
            update.Parameters.AddWithValue("@sourceWindowTitle", sourceWindowTitle);
            update.Parameters.AddWithValue("@id", existing.Id);
            update.ExecuteNonQuery();
            entry = existing with
            {
                SourceProcess = sourceProcess,
                SourceWindowTitle = sourceWindowTitle,
                SeenCount = existing.SeenCount + 1,
                LastSeenAtMs = stamp,
            };
        }
        else
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO clipboard_history
                    (content, plain_preview, content_sha256, source_process, source_window_title, pinned, seen_count, created_at_ms, last_seen_at_ms, kind, image_path, image_width, image_height)
                VALUES
                    (@content, @preview, @hash, @sourceProcess, @sourceWindowTitle, 0, 1, @stamp, @stamp, @kind, @imagePath, @imageWidth, @imageHeight)
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
            var id = (long)insert.ExecuteScalar()!;
            entry = new ClipboardEntry(id, content, preview, sourceProcess, sourceWindowTitle, false, 1, stamp, stamp,
                draft.Kind, draft.ImagePath, draft.ImageWidth, draft.ImageHeight);
        }

        TrimUnpinned(connection, transaction, maxEntries, 0);
        transaction.Commit();

        // With MaxEntries 0 a new unpinned row is trimmed away in the same transaction; only report
        // entries the store actually keeps. An inserted or promoted row is the table-wide newest,
        // so any positive cap always keeps it.
        return entry.Pinned || maxEntries > 0 ? entry : null;
    }

    private List<ClipboardEntry> GetEntriesCore(CancellationToken cancellationToken)
    {
        var entries = new List<ClipboardEntry>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {EntryColumns} FROM clipboard_history ORDER BY pinned DESC, last_seen_at_ms DESC, id DESC";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(MapEntry(reader));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return entries;
    }

    private List<ClipboardEntry> SearchCore(string query, CancellationToken cancellationToken)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return GetEntriesCore(cancellationToken);

        var longTerms = terms.Where(term => term.EnumerateRunes().Count() >= 3).ToArray();
        var shortTerms = terms.Where(term => term.EnumerateRunes().Count() < 3).ToArray();
        var entries = new List<ClipboardEntry>();

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var conditions = new List<string>();
        if (longTerms.Length > 0)
        {
            conditions.Add("clipboard_history_fts MATCH @ftsQuery");
            command.Parameters.AddWithValue("@ftsQuery", string.Join(" AND ", longTerms.Select(QuoteFtsTerm)));
        }

        for (var index = 0; index < shortTerms.Length; index++)
        {
            conditions.Add($"h.content LIKE @short{index} ESCAPE '\\' COLLATE NOCASE");
            command.Parameters.AddWithValue($"@short{index}", "%" + EscapeLike(shortTerms[index]) + "%");
        }

        command.CommandText = $"""
            SELECT {SearchEntryColumns}
            FROM clipboard_history h
            {(longTerms.Length > 0 ? "JOIN clipboard_history_fts fts ON fts.rowid = h.id" : string.Empty)}
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY h.pinned DESC, h.last_seen_at_ms DESC, h.id DESC
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(MapEntry(reader));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return entries;
    }

    private static string QuoteFtsTerm(string term) => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EscapeLike(string term) => term.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private void SetPinnedCore(long id, bool pinned)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clipboard_history SET pinned = @pinned WHERE id = @id";
        command.Parameters.AddWithValue("@pinned", pinned);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    private void DeleteCore(long id)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var images = DeleteWhere(connection, transaction, "id = @id", ("@id", id));
        transaction.Commit();
        UnlinkImages(images);
    }

    private void ClearCore()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var images = DeleteWhere(connection, transaction, "1 = 1");
        transaction.Commit();
        UnlinkImages(images);
    }

    private void TrimCore(int maxEntries, int retentionDays)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        TrimUnpinned(connection, transaction, maxEntries, retentionDays);
        transaction.Commit();
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

        var images = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            if (path.Length > 0) images.Add(path);
        }
        return images;
    }

    /// <summary>Deletes matching rows and returns their image file names. The caller commits before
    /// unlinking: a crash between commit and unlink leaves an orphan file (cleaned by the runtime's
    /// sweep) but never a row pointing at a missing file.</summary>
    private static List<string> DeleteWhere(SqliteConnection connection, SqliteTransaction transaction,
        string where, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM clipboard_history WHERE {where} RETURNING image_path";
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        var images = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var path = reader.GetString(0);
            if (path.Length > 0) images.Add(path);
        }
        return images;
    }

    private void UnlinkImages(List<string> fileNames)
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

    private static ClipboardEntry MapEntry(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetBoolean(5),
        reader.GetInt64(6),
        reader.GetInt64(7),
        reader.GetInt64(8),
        (ClipboardEntryKind)reader.GetInt64(9),
        reader.GetString(10),
        reader.GetInt32(11),
        reader.GetInt32(12));
}
