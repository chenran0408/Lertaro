using System.Text;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Sanitizes and builds SQLite FTS5 trigram MATCH query syntax from user search terms.
/// </summary>
public static class DatabaseFtsQueryHelper
{
    private static readonly char[] TrimChars = ['"', '*', '^', ':', '(', ')', '{', '}', '[', ']'];

    internal static bool IsShortTerm(string token)
    {
        var count = 0;
        foreach (var _ in token.EnumerateRunes())
            if (++count == 3) return false;
        return true;
    }

    public static string BuildFtsQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var rawToken in tokens)
        {
            var cleaned = rawToken.Trim(TrimChars).Replace("\"", "\"\"").Trim();
            if (cleaned.Length == 0) continue;

            if (sb.Length > 0) sb.Append(" AND ");

            // FTS5's trigram tokenizer indexes three-character sequences only, so a one- or
            // two-character term has no entry to match against: it can never match anything in
            // this index, with or without a trailing '*'. The prefix form below is still emitted
            // for completeness, but nothing in the search path relies on it -- DatabaseSearchHelper
            // answers any query holding such a term with its content scan instead.
            if (IsShortTerm(cleaned))
            {
                sb.Append('"').Append(cleaned).Append("\"*");
            }
            else
            {
                sb.Append('"').Append(cleaned).Append('"');
            }
        }
        return sb.ToString();
    }
}
