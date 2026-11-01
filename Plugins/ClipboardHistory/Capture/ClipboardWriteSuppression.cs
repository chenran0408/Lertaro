namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Skips the WM_CLIPBOARDUPDATE traffic produced by our own WriteTextAsync call.
// Two mechanisms: a scoped flag for the notifications WPF raises reentrantly
// inside SetDataObject, and a recorded post-write sequence number for events
// delivered afterwards. Matching uses the exact sequence number instead of a
// boolean skip-next flag: an unrelated app's update has a different sequence
// number and is never dropped.
internal sealed class ClipboardWriteSuppression
{
    private uint _afterSequence;
    private bool _writing;
    private bool _pending;

    public void BeginWrite() => _writing = true;

    // The caller verifies the current owner belongs to this process before recording
    // a post-write sequence; an intervening external writer clears the scope instead.
    public void EndWrite(uint sequenceBefore, uint sequenceAfter)
    {
        _writing = false;
        _afterSequence = sequenceAfter;
        // A write that changed nothing raises no WM_CLIPBOARDUPDATE, so only a
        // sequence change arms the skip.
        _pending = sequenceAfter != sequenceBefore;
    }

    public void Clear()
    {
        _writing = false;
        _pending = false;
    }

    public bool ShouldSkipCapture(uint currentSequence)
    {
        // Reentrant notifications raised by our own in-flight SetDataObject.
        if (_writing) return true;
        if (!_pending) return false;
        // The same sequence change can notify more than once: keep suppressing
        // while it still matches, and end the suppression window at the first
        // different sequence so an unrelated later update is captured.
        if (currentSequence == _afterSequence) return true;
        _pending = false;
        return false;
    }
}
