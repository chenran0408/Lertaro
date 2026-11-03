namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>What a history entry holds. The store keeps one row per capture kind.</summary>
internal enum ClipboardEntryKind
{
    Text = 0,
    Image = 1,
    FilePaths = 2,
}

/// <summary>
/// One stored clipboard record of any kind.
/// </summary>
/// <param name="Id">Row id; the stable handle used by action providers and store mutations.</param>
/// <param name="Content">For Text, the full stored text already truncated to the configured
/// MaxTextLength. For FilePaths, the absolute paths joined by line breaks (the FTS-searchable text).
/// For Image, empty.</param>
/// <param name="PlainPreview">At most 200 characters of single-line list display text with line
/// breaks flattened to spaces.</param>
/// <param name="SourceProcess">Clipboard owner's process name when the content was captured.</param>
/// <param name="SourceWindowTitle">Clipboard owner's main window title when the content was captured.</param>
/// <param name="Pinned">Pinned entries are exempt from the unpinned MaxEntries cap and the
/// retention window.</param>
/// <param name="SeenCount">How many times this exact content has been captured (dedup merges).</param>
/// <param name="CreatedAtMs">Unix milliseconds of the first capture.</param>
/// <param name="LastSeenAtMs">Unix milliseconds of the most recent capture; monotonically increases
/// on dedup promotion so re-copied content always sorts back to the top, even within the same
/// millisecond.</param>
/// <param name="Kind">Which payload the row holds.</param>
/// <param name="ImagePath">For Image, the file name (not the full path) of the content-addressed
/// PNG under the runtime's images directory; empty otherwise.</param>
/// <param name="ImageWidth">For Image, the pixel width of the captured bitmap; 0 otherwise.</param>
/// <param name="ImageHeight">For Image, the pixel height of the captured bitmap; 0 otherwise.</param>
internal sealed record ClipboardEntry(
    long Id,
    string Content,
    string PlainPreview,
    string SourceProcess,
    string SourceWindowTitle,
    bool Pinned,
    long SeenCount,
    long CreatedAtMs,
    long LastSeenAtMs,
    ClipboardEntryKind Kind = ClipboardEntryKind.Text,
    string ImagePath = "",
    int ImageWidth = 0,
    int ImageHeight = 0)
{
    /// <summary>The individual paths behind a FilePaths entry, in stored order. Returns empty for
    /// other kinds. ponytail: split on demand instead of a stored list column -- the join is the
    /// FTS-searchable Content, and no caller needs the list faster than a split.</summary>
    public IReadOnlyList<string> PathList => Kind == ClipboardEntryKind.FilePaths
        ? Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : [];
}
