using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Password managers and similar apps mark a copy as sensitive with custom clipboard formats
// instead of (or in addition to) an entry on the process exclusion list. Windows' own cloud
// clipboard and the mature managers (Ditto, CopyQ) honor these markers, so a stored password
// never lands in any history: skip the capture when "ExcludeClipboardContentFromMonitorProcessing"
// is present, or when "CanIncludeInClipboardHistory" is present with the value 0.
// Undecidable (the clipboard could not be opened within the retry budget) fails closed -- a
// missed capture is recoverable by copying again; a captured password is not.
internal static class ClipboardSensitivity
{
    internal const int MaxRetries = 2; // same bounded open budget as the readers
    internal const int RetryIntervalMs = 50;

    private const string ExcludeAllName = "ExcludeClipboardContentFromMonitorProcessing";
    private const string IncludeInHistoryName = "CanIncludeInClipboardHistory";

    private static int _excludeAllFormat = -1;
    private static int _includeInHistoryFormat = -1;

    public static bool ShouldSkip(IClipboardNative native, nint openWindow, uint expectedSequence, nint expectedOwner)
    {
        var excludeAll = RegisteredFormat(native, ref _excludeAllFormat, ExcludeAllName);
        var includeInHistory = RegisteredFormat(native, ref _includeInHistoryFormat, IncludeInHistoryName);
        if (excludeAll == 0 && includeInHistory == 0) return false; // nothing to look for

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0) Thread.Sleep(RetryIntervalMs);
            if (!native.OpenClipboard(openWindow)) continue;
            try
            {
                if (native.GetClipboardSequenceNumber() != expectedSequence ||
                    native.GetClipboardOwner() != expectedOwner) return false; // changed: the next update owns it
                if (excludeAll != 0 && native.IsClipboardFormatAvailable(excludeAll)) return true;
                if (includeInHistory != 0 && native.IsClipboardFormatAvailable(includeInHistory))
                    return ReadHistoryFlag(native, includeInHistory) == 0;
                return false;
            }
            finally
            {
                native.CloseClipboard();
            }
        }
        return true; // undecidable: fail closed
    }

    private static int RegisteredFormat(IClipboardNative native, ref int cached, string name)
    {
        if (cached != -1) return cached;
        cached = (int)native.RegisterClipboardFormat(name);
        return cached;
    }

    // The marker is a DWORD on the clipboard: 0 = keep out of history, anything else = allowed.
    // A marker without a readable payload is treated as allowed -- only an explicit zero excludes.
    private static uint ReadHistoryFlag(IClipboardNative native, int format)
    {
        var handle = native.GetClipboardData(format);
        if (handle == nint.Zero || native.GlobalSize(handle) < sizeof(uint)) return 1;
        var ptr = native.GlobalLock(handle);
        if (ptr == nint.Zero) return 1;
        try
        {
            return (uint)Marshal.ReadInt32(ptr);
        }
        finally
        {
            native.GlobalUnlock(handle);
        }
    }
}
