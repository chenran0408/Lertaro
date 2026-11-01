using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Seam over the Win32 clipboard/window APIs used by the monitor so the open-retry
// and bounded-read logic can be tested with a fake instead of the real clipboard.
internal interface IClipboardNative
{
    public const int CfUnicodeText = 13;

    uint GetClipboardSequenceNumber();
    nint GetClipboardOwner();
    uint GetWindowThreadProcessId(nint hWnd, out int processId);
    bool AddClipboardFormatListener(nint hWnd);
    bool RemoveClipboardFormatListener(nint hWnd);
    bool IsClipboardFormatAvailable(int format);
    bool OpenClipboard(nint hWndNewOwner);
    bool CloseClipboard();
    nint GetClipboardData(int format);
    nuint GlobalSize(nint hMem); // SIZE_T
    nint GlobalLock(nint hMem);
    bool GlobalUnlock(nint hMem);
}

internal sealed class Win32ClipboardNative : IClipboardNative
{
    public uint GetClipboardSequenceNumber() => GetClipboardSequenceNumberNative();
    public nint GetClipboardOwner() => GetClipboardOwnerNative();

    public uint GetWindowThreadProcessId(nint hWnd, out int processId)
    {
        var thread = GetWindowThreadProcessIdNative(hWnd, out var pid);
        processId = unchecked((int)pid);
        return thread;
    }

    public bool AddClipboardFormatListener(nint hWnd) => AddClipboardFormatListenerNative(hWnd);
    public bool RemoveClipboardFormatListener(nint hWnd) => RemoveClipboardFormatListenerNative(hWnd);
    public bool IsClipboardFormatAvailable(int format) => IsClipboardFormatAvailableNative(format);
    public bool OpenClipboard(nint hWndNewOwner) => OpenClipboardNative(hWndNewOwner);
    public bool CloseClipboard() => CloseClipboardNative();
    public nint GetClipboardData(int format) => GetClipboardDataNative(format);
    public nuint GlobalSize(nint hMem) => GlobalSizeNative(hMem);
    public nint GlobalLock(nint hMem) => GlobalLockNative(hMem);
    public bool GlobalUnlock(nint hMem) => GlobalUnlockNative(hMem);

    [DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
    private static extern uint GetClipboardSequenceNumberNative();

    [DllImport("user32.dll", EntryPoint = "GetClipboardOwner", SetLastError = true)]
    private static extern nint GetClipboardOwnerNative();

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetWindowThreadProcessIdNative(nint hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "AddClipboardFormatListener", SetLastError = true)]
    private static extern bool AddClipboardFormatListenerNative(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "RemoveClipboardFormatListener", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListenerNative(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "IsClipboardFormatAvailable", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailableNative(int format);

    [DllImport("user32.dll", EntryPoint = "OpenClipboard", SetLastError = true)]
    private static extern bool OpenClipboardNative(nint hWndNewOwner);

    [DllImport("user32.dll", EntryPoint = "CloseClipboard", SetLastError = true)]
    private static extern bool CloseClipboardNative();

    [DllImport("user32.dll", EntryPoint = "GetClipboardData", SetLastError = true)]
    private static extern nint GetClipboardDataNative(int format);

    [DllImport("kernel32.dll", EntryPoint = "GlobalSize", SetLastError = true)]
    private static extern nuint GlobalSizeNative(nint hMem);

    [DllImport("kernel32.dll", EntryPoint = "GlobalLock", SetLastError = true)]
    private static extern nint GlobalLockNative(nint hMem);

    [DllImport("kernel32.dll", EntryPoint = "GlobalUnlock", SetLastError = true)]
    private static extern bool GlobalUnlockNative(nint hMem);
}
