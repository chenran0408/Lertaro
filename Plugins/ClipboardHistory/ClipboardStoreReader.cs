using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// The read half of <see cref="ClipboardStore"/>: newest-first listing and the SQLite FTS/LIKE
/// hybrid text search. Split from the store purely for the per-file line limit; holds no state.
/// </summary>
internal static class ClipboardStoreReader
{
    public static List<ClipboardEntry> GetEntries(ClipboardDatabase database, CancellationToken cancellationToken)
    {
        var entries = new List<ClipboardEntry>();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {ClipboardEntryColumns.EntryColumns} FROM clipboard_history ORDER BY pinned DESC, last_seen_at_ms DESC, id DESC";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(ClipboardEntryColumns.Map(reader));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return entries;
    }

    public static List<ClipboardEntry> Search(ClipboardDatabase database, string query, CancellationToken cancellationToken)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return GetEntries(database, cancellationToken);

        // FTS trigram needs >= 3 characters per term; shorter ones fall back to a LIKE scan. Mixed
        // queries require both halves (AND), matching the panel's word-per-word expectations.
        var longTerms = terms.Where(term => term.EnumerateRunes().Count() >= 3).ToArray();
        var shortTerms = terms.Where(term => term.EnumerateRunes().Count() < 3).ToArray();
        var entries = new List<ClipboardEntry>();

        using var connection = database.OpenConnection();
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
            SELECT {ClipboardEntryColumns.SearchEntryColumns}
            FROM clipboard_history h
            {(longTerms.Length > 0 ? "JOIN clipboard_history_fts fts ON fts.rowid = h.id" : string.Empty)}
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY h.pinned DESC, h.last_seen_at_ms DESC, h.id DESC
            """;
        using var result = command.ExecuteReader();
        while (result.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(ClipboardEntryColumns.Map(result));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return entries;
    }

    private static string QuoteFtsTerm(string term) => "\"" + term.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string EscapeLike(string term) => term.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);
}
