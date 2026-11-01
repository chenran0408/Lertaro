using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// SQLite-backed clipboard history storage. Owns dedup (same text merges into one row and is
/// promoted back to the top with a monotonically increasing LastSeenAtMs), text truncation, the
/// unpinned entry cap (pins are exempt) and corruption recovery on initialization.
/// </summary>
/// <remarks>
/// Microsoft.Data.Sqlite is synchronous, so every operation runs on the thread pool via Task.Run
/// while callers await; combined with the per-store SemaphoreSlim this keeps SQLite calls serialized
/// and never blocks a UI thread on disk I/O. Connections are opened per call and disposed: no
/// IDisposable on the store itself, and pooling is off so no pooled connection keeps the database
/// file open (corruption recovery must be able to rename it aside).
/// </remarks>
internal sealed class ClipboardStore(string databasePath)
{
    private const int PreviewLength = 200;

    // Column order shared by every SELECT; MapEntry depends on it.
    private const string EntryColumns =
        "id, content, plain_preview, source_process, source_window_title, pinned, seen_count, created_at_ms, last_seen_at_ms";

    private readonly ClipboardDatabase _database = new(databasePath);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync(InitializeCore, cancellationToken);

    public Task<ClipboardEntry?> AddAsync(string content, string sourceProcess, string sourceWindowTitle,
        int maxEntries, int maxTextLength, CancellationToken cancellationToken = default) =>
        RunLockedAsync(
            () => AddCore(content, sourceProcess ?? string.Empty, sourceWindowTitle ?? string.Empty,
                maxEntries, maxTextLength),
            cancellationToken);

    public Task<IReadOnlyList<ClipboardEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync<IReadOnlyList<ClipboardEntry>>(() => GetEntriesCore(cancellationToken), cancellationToken);

    public Task SetPinnedAsync(long id, bool pinned, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => SetPinnedCore(id, pinned), cancellationToken);

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => DeleteCore(id), cancellationToken);

    public Task TrimAsync(int maxEntries, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => TrimCore(Math.Max(0, maxEntries)), cancellationToken);

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

    private ClipboardEntry? AddCore(string content, string sourceProcess, string sourceWindowTitle,
        int maxEntries, int maxTextLength)
    {
        maxEntries = Math.Max(0, maxEntries);
        maxTextLength = Math.Max(0, maxTextLength);
        var truncated = TruncateAtCharBoundary(content, maxTextLength);
        if (truncated.Length == 0) return null;

        var preview = BuildPreview(truncated);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(truncated));
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
                $"SELECT {EntryColumns} FROM clipboard_history WHERE content_sha256 = @hash AND content = @content ORDER BY id LIMIT 1";
            select.Parameters.AddWithValue("@hash", hash);
            select.Parameters.AddWithValue("@content", truncated);
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
                    (content, plain_preview, content_sha256, source_process, source_window_title, pinned, seen_count, created_at_ms, last_seen_at_ms)
                VALUES
                    (@content, @preview, @hash, @sourceProcess, @sourceWindowTitle, 0, 1, @stamp, @stamp)
                RETURNING id
                """;
            insert.Parameters.AddWithValue("@content", truncated);
            insert.Parameters.AddWithValue("@preview", preview);
            insert.Parameters.AddWithValue("@hash", hash);
            insert.Parameters.AddWithValue("@sourceProcess", sourceProcess);
            insert.Parameters.AddWithValue("@sourceWindowTitle", sourceWindowTitle);
            insert.Parameters.AddWithValue("@stamp", stamp);
            var id = (long)insert.ExecuteScalar()!;
            entry = new ClipboardEntry(id, truncated, preview, sourceProcess, sourceWindowTitle, false, 1, stamp, stamp);
        }

        TrimUnpinned(connection, transaction, maxEntries);
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
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clipboard_history WHERE id = @id";
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    private void TrimCore(int maxEntries)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        TrimUnpinned(connection, transaction, maxEntries);
        transaction.Commit();
    }

    private static void TrimUnpinned(SqliteConnection connection, SqliteTransaction transaction, int maxEntries)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Pinned entries are exempt from the cap: it applies to the unpinned rows only, of which
        // the newest by (LastSeenAtMs, Id) are kept. Tie-breaking by Id is total, so two rows that
        // share a millisecond never both fall outside the cap while an older one survives.
        command.CommandText = """
            DELETE FROM clipboard_history
            WHERE pinned = 0 AND id NOT IN (
                SELECT id FROM clipboard_history
                WHERE pinned = 0
                ORDER BY last_seen_at_ms DESC, id DESC
                LIMIT @maxEntries
            )
            """;
        command.Parameters.AddWithValue("@maxEntries", maxEntries);
        command.ExecuteNonQuery();
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
        reader.GetInt64(8));

    private static string BuildPreview(string content)
    {
        var trimmed = content.AsSpan().Trim();
        var length = Math.Min(trimmed.Length, PreviewLength);
        if (length > 0 && char.IsHighSurrogate(trimmed[length - 1])) length--;
        var preview = trimmed[..length].ToString();
        return string.Create(preview.Length, preview, (output, text) =>
        {
            for (var index = 0; index < text.Length; index++)
                output[index] = char.IsWhiteSpace(text[index]) ? ' ' : text[index];
        });
    }

    private static string TruncateAtCharBoundary(string text, int maxLength)
    {
        if (maxLength <= 0) return string.Empty;
        if (text.Length <= maxLength) return text;
        // A cut landing between the halves of a surrogate pair backs off one char so the stored
        // text never ends in a lone high surrogate.
        var end = maxLength;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end];
    }
}
