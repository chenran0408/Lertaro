using System.IO;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// SQLite-backed clipboard history storage for all entry kinds. Owns the serialization shell (one
/// SemaphoreSlim, synchronous SQLite work on the thread pool, connections opened per call so
/// corruption recovery can rename the file aside) and delegates the actual work:
/// <see cref="ClipboardStoreWriter"/> for inserts/deletes/trims, <see cref="ClipboardStoreReader"/>
/// for listing and search.
/// </summary>
/// <remarks>
/// Image pixels live as content-addressed PNG files under <paramref name="imagesDirectory"/>; the
/// writer unlinks a row's PNG when the row is deleted.
/// </remarks>
internal sealed class ClipboardStore(string databasePath, string imagesDirectory)
{
    private readonly ClipboardDatabase _database = new(databasePath, imagesDirectory);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync(InitializeCore, cancellationToken);

    public Task<ClipboardEntry?> AddAsync(ClipboardEntryDraft draft, string sourceProcess, string sourceWindowTitle,
        int maxEntries, int maxTextLength, CancellationToken cancellationToken = default) =>
        RunLockedAsync(
            () => ClipboardStoreWriter.Add(_database, draft, sourceProcess ?? string.Empty,
                sourceWindowTitle ?? string.Empty, maxEntries, maxTextLength),
            cancellationToken);

    public Task<IReadOnlyList<ClipboardEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync<IReadOnlyList<ClipboardEntry>>(
            () => ClipboardStoreReader.GetEntries(_database, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<ClipboardEntry>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        RunLockedAsync<IReadOnlyList<ClipboardEntry>>(
            () => ClipboardStoreReader.Search(_database, query ?? string.Empty, cancellationToken), cancellationToken);

    public Task SetPinnedAsync(long id, bool pinned, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => SetPinnedCore(id, pinned), cancellationToken);

    public Task DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => ClipboardStoreWriter.Delete(_database, id), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => ClipboardStoreWriter.Clear(_database), cancellationToken);

    public Task TrimAsync(int maxEntries, int retentionDays, CancellationToken cancellationToken = default) =>
        RunLockedAsync(() => ClipboardStoreWriter.Trim(_database, maxEntries, retentionDays), cancellationToken);

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

    private void SetPinnedCore(long id, bool pinned)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clipboard_history SET pinned = @pinned WHERE id = @id";
        command.Parameters.AddWithValue("@pinned", pinned);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }
}
