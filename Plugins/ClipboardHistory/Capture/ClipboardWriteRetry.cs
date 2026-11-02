using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Other processes can hold the clipboard open briefly; SetDataObject then fails
// with CLIPBRD_E_CANT_OPEN (0x800401D0). Retry the same way the read path does.
internal static class ClipboardWriteRetry
{
    public static void Execute(Action write)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { write(); return; }
            catch (COMException ex) when (
                ex.HResult == unchecked((int)0x800401D0) &&
                attempt < ClipboardTextReader.MaxRetries)
            {
                Thread.Sleep(ClipboardTextReader.RetryIntervalMs);
            }
        }
    }
}
