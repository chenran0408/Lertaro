using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// The SELECT column list shared by every reader and the matching row mapper. Column order is a
/// contract between the two -- add a column here and to <see cref="Map"/> in the same change.
/// </summary>
internal static class ClipboardEntryColumns
{
    public const string EntryColumns =
        "id, content, plain_preview, source_process, source_window_title, pinned, seen_count, created_at_ms, last_seen_at_ms, kind, image_path, image_width, image_height, html, note, paste_count";
    public const string SearchEntryColumns =
        "h.id, h.content, h.plain_preview, h.source_process, h.source_window_title, h.pinned, h.seen_count, h.created_at_ms, h.last_seen_at_ms, h.kind, h.image_path, h.image_width, h.image_height, h.html, h.note, h.paste_count";

    public static ClipboardEntry Map(SqliteDataReader reader) => new(
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
        reader.GetInt32(12),
        reader.GetString(13),
        reader.GetString(14),
        reader.GetInt64(15));
}
