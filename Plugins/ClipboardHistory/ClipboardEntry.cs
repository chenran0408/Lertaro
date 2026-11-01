namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// One stored clipboard text record.
/// </summary>
/// <param name="Id">Row id; the stable handle used by action providers and store mutations.</param>
/// <param name="Content">Full stored text, already truncated to the configured MaxTextLength.</param>
/// <param name="PlainPreview">At most 200 characters of <paramref name="Content"/> with line breaks
/// flattened to spaces, for single-line list display.</param>
/// <param name="SourceProcess">Clipboard owner's process name when the text was captured.</param>
/// <param name="SourceWindowTitle">Clipboard owner's main window title when the text was captured.</param>
/// <param name="Pinned">Pinned entries are exempt from the unpinned MaxEntries cap.</param>
/// <param name="SeenCount">How many times this exact text has been captured (dedup merges).</param>
/// <param name="CreatedAtMs">Unix milliseconds of the first capture.</param>
/// <param name="LastSeenAtMs">Unix milliseconds of the most recent capture; monotonically increases
/// on dedup promotion so re-copied text always sorts back to the top, even within the same
/// millisecond.</param>
internal sealed record ClipboardEntry(
    long Id,
    string Content,
    string PlainPreview,
    string SourceProcess,
    string SourceWindowTitle,
    bool Pinned,
    long SeenCount,
    long CreatedAtMs,
    long LastSeenAtMs);
