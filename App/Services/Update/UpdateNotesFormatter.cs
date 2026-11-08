using System.Text.RegularExpressions;

namespace Lertaro.App.Services.Update;

/// <summary>One rendered line of the release notes: either body text or a section header.</summary>
public readonly record struct UpdateNotesLine(string Text, bool IsHeader);

/// <summary>
/// Turns a GitHub release body (the markdown the publish pipeline copied from release-notes/*.md)
/// into plain display lines for the About page's "what's new" area. Handles exactly the shapes our
/// own notes use -- ATX headers, dash bullets, bold/code/link spans, horizontal rules, and the
/// publish job's appended &lt;details&gt; checksum block -- nothing more; anything else passes
/// through with its markdown markers stripped.
/// </summary>
public static partial class UpdateNotesFormatter
{
    // The publish job appends "<details><summary>文件校验信息…</summary> … </details>" with SHA-256
    // hashes; that is release-attestation noise in a "what's new" panel, so it never renders.
    [GeneratedRegex(@"<details>.*?</details>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DetailsBlock();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldSpan();

    public static IReadOnlyList<UpdateNotesLine> Format(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        var cleaned = DetailsBlock().Replace(markdown, string.Empty);
        var lines = new List<UpdateNotesLine>();
        foreach (var raw in cleaned.Split('\n'))
        {
            var line = raw.TrimEnd().Trim();
            if (line.Length == 0) continue;
            if (line is "---" or "***" or "___") continue;

            var header = false;
            var hashCount = 0;
            while (hashCount < line.Length && line[hashCount] == '#') hashCount++;
            if (hashCount is > 0 and <= 6 && hashCount < line.Length && line[hashCount] == ' ')
            {
                header = true;
                line = line[(hashCount + 1)..].Trim();
            }
            else if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
            {
                line = "\u2022 " + line[2..].TrimStart();
            }

            line = BoldSpan().Replace(line, "$1");
            line = MarkdownLink().Replace(line, "$1");
            line = line.Replace("`", string.Empty);
            if (line.Length == 0) continue;
            lines.Add(new UpdateNotesLine(line, header));
        }
        return lines;
    }
}
