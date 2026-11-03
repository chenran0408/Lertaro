using System.IO;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// Pure mapping helpers for <see cref="ClipboardStore"/>: per-kind previews, the FTS-searchable
/// content string, truncation and hashing. No state, no database, no clipboard -- everything here is
/// unit-testable on plain data.
/// </summary>
internal static class ClipboardStoreSupport
{
    private const int PreviewLength = 200;
    private const int MaxListedPathsInPreview = 3;

    /// <summary>The text that goes into the content column: text as captured; for a file list the
    /// normalized (case-insensitive) joined paths so FTS can find copies by file name; empty for an
    /// image, whose pixels live on disk and whose identity is the PNG hash.</summary>
    public static string BuildContent(ClipboardEntryDraft draft) => draft.Kind switch
    {
        ClipboardEntryKind.FilePaths => ClipboardEntryDraft.JoinPaths(ClipboardEntryDraft.ToPathList(draft.Content)),
        ClipboardEntryKind.Image => string.Empty,
        _ => draft.Content,
    };

    /// <summary>The single-line list preview. Language-neutral by design: the panel wraps labels
    /// around it where it needs to.</summary>
    public static string BuildPreview(ClipboardEntryDraft draft)
    {
        var content = BuildContent(draft);
        if (draft.Kind == ClipboardEntryKind.Image)
            return Flatten($"{draft.ImageWidth}\u00d7{draft.ImageHeight}");
        if (draft.Kind == ClipboardEntryKind.FilePaths)
        {
            var paths = ClipboardEntryDraft.ToPathList(content);
            var names = paths.Select(Path.GetFileName).Take(MaxListedPathsInPreview);
            var prefix = paths.Length > MaxListedPathsInPreview
                ? $"{paths.Length} \u00b7 "
                : string.Empty;
            return Flatten($"{prefix}{string.Join(", ", names)}");
        }
        return BuildPlainPreview(content);
    }

    public static string BuildPlainPreview(string content)
    {
        var trimmed = content.AsSpan().Trim();
        var length = Math.Min(trimmed.Length, PreviewLength);
        if (length > 0 && char.IsHighSurrogate(trimmed[length - 1])) length--;
        return Flatten(trimmed[..length].ToString());
    }

    private static string Flatten(string text) => string.Create(text.Length, text, (output, value) =>
    {
        for (var index = 0; index < value.Length; index++)
            output[index] = char.IsWhiteSpace(value[index]) ? ' ' : value[index];
    });

    public static string TruncateText(string text, int maxLength)
    {
        if (maxLength <= 0) return string.Empty;
        if (text.Length <= maxLength) return text;
        // A cut landing between the halves of a surrogate pair backs off one char so the stored
        // text never ends in a lone high surrogate.
        var end = maxLength;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end];
    }

    public static string HashToHex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
}
