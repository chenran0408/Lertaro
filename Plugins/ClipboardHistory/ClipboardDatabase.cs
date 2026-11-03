using System.IO;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// Database-level plumbing for <see cref="ClipboardStore"/>: connection opening, schema creation
/// and corruption recovery. Split out purely to keep ClipboardStore under the per-file line limit;
/// it has no state of its own and only touches the database file it is constructed with.
/// </summary>
internal sealed class ClipboardDatabase(string databasePath, string imagesDirectory)
{
    /// <summary>Where content-addressed image files live; the writer unlinks deleted rows' files
    /// here, so the plumbing travels with the connection factory that owns the database path.</summary>
    public string ImagesDirectory { get; } = imagesDirectory;

    // Primary result codes under which a database file is proven corrupt; every other error
    // (locked file, disk full, bad path, ...) must propagate to the caller untouched.
    internal const int SqliteCorrupt = 11;
    internal const int SqliteNotADatabase = 26;

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS clipboard_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            content TEXT NOT NULL,
            plain_preview TEXT NOT NULL,
            content_sha256 BLOB NOT NULL,
            source_process TEXT NOT NULL DEFAULT '',
            source_window_title TEXT NOT NULL DEFAULT '',
            pinned INTEGER NOT NULL DEFAULT 0,
            seen_count INTEGER NOT NULL DEFAULT 1,
            created_at_ms INTEGER NOT NULL,
            last_seen_at_ms INTEGER NOT NULL,
            kind INTEGER NOT NULL DEFAULT 0,
            image_path TEXT NOT NULL DEFAULT '',
            image_width INTEGER NOT NULL DEFAULT 0,
            image_height INTEGER NOT NULL DEFAULT 0,
            html TEXT NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS idx_clipboard_last_seen ON clipboard_history(pinned DESC, last_seen_at_ms DESC);
        CREATE INDEX IF NOT EXISTS idx_clipboard_sha ON clipboard_history(content_sha256);
        CREATE VIRTUAL TABLE IF NOT EXISTS clipboard_history_fts USING fts5(
            content,
            content='clipboard_history',
            content_rowid='id',
            tokenize='trigram'
        );
        CREATE TRIGGER IF NOT EXISTS clipboard_history_ai AFTER INSERT ON clipboard_history BEGIN
            INSERT INTO clipboard_history_fts(rowid, content) VALUES (new.id, new.content);
        END;
        CREATE TRIGGER IF NOT EXISTS clipboard_history_ad AFTER DELETE ON clipboard_history BEGIN
            INSERT INTO clipboard_history_fts(clipboard_history_fts, rowid, content)
            VALUES ('delete', old.id, old.content);
        END;
        CREATE TRIGGER IF NOT EXISTS clipboard_history_au AFTER UPDATE OF content ON clipboard_history BEGIN
            INSERT INTO clipboard_history_fts(clipboard_history_fts, rowid, content)
            VALUES ('delete', old.id, old.content);
            INSERT INTO clipboard_history_fts(rowid, content) VALUES (new.id, new.content);
        END;
        """;

    // Columns added after the text-only schema. Existing databases created by 6.3.x lack them;
    // EnsureSchema adds any that PRAGMA table_info does not list, with the same defaults the CREATE
    // above spells out, so a pre-kind database migrates in place and in order.
    private static readonly (string Name, string Sql)[] AddedColumns =
    [
        ("kind", "ALTER TABLE clipboard_history ADD COLUMN kind INTEGER NOT NULL DEFAULT 0"),
        ("image_path", "ALTER TABLE clipboard_history ADD COLUMN image_path TEXT NOT NULL DEFAULT ''"),
        ("image_width", "ALTER TABLE clipboard_history ADD COLUMN image_width INTEGER NOT NULL DEFAULT 0"),
        ("image_height", "ALTER TABLE clipboard_history ADD COLUMN image_height INTEGER NOT NULL DEFAULT 0"),
        ("html", "ALTER TABLE clipboard_history ADD COLUMN html TEXT NOT NULL DEFAULT ''"),
    ];

    internal SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Pooling would keep the file open across calls, which would block renaming it aside
            // during corruption recovery.
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
        MigrateAddedColumns(connection);

        using var consistency = connection.CreateCommand();
        consistency.CommandText = """
            SELECT (SELECT COUNT(*) FROM clipboard_history_fts) <> (SELECT COUNT(*) FROM clipboard_history)
            """;
        if (Convert.ToInt32(consistency.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0)
        {
            using var rebuild = connection.CreateCommand();
            rebuild.CommandText = "INSERT INTO clipboard_history_fts(clipboard_history_fts) VALUES ('rebuild')";
            rebuild.ExecuteNonQuery();
        }
    }

    private static void MigrateAddedColumns(SqliteConnection connection)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(clipboard_history)";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
                present.Add(reader.GetString(1));
        }

        foreach (var (name, sql) in AddedColumns)
        {
            if (present.Contains(name)) continue;
            using var alter = connection.CreateCommand();
            alter.CommandText = sql;
            alter.ExecuteNonQuery();
        }
    }

    internal void MoveCorruptDatabaseAside()
    {
        var suffix = $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        // Sidecars first, then the database itself, and only recreate after every rename succeeded:
        // a failure here propagates and leaves the corrupt files in place instead of ending up with
        // a fresh database whose recovery evidence is half gone.
        foreach (var sidecar in (string[])["-journal", "-wal", "-shm"])
        {
            var sidecarPath = databasePath + sidecar;
            if (File.Exists(sidecarPath)) File.Move(sidecarPath, sidecarPath + suffix);
        }

        File.Move(databasePath, databasePath + suffix);
    }

    internal static long GetMaxStamp(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(last_seen_at_ms), 0) FROM clipboard_history";
        return (long)command.ExecuteScalar()!;
    }
}
