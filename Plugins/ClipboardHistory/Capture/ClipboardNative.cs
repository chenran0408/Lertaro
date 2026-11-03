using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory.Capture;

// Seam over the Win32 clipboard/window APIs used by the monitor so the open-retry
// and bounded-read logic can be tested with a fake instead of the real clipboard.
internal interface IClipboardNative
{
    public const int CfUnicodeText = 13;
    public const int CfHDrop = 15;
    public const int CfBitmap = 2;
    public const int CfDib = 8;
    public const int CfDibV5 = 17;

    /// <summary>Registered format carrying the browser/Office HTML flavor ("HTML Format").</summary>
    public const string HtmlFormatName = "HTML Format";

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

    uint RegisterClipboardFormat(string formatName);

    /// <summary>DragQueryFileW with index 0xFFFFFFFF: the number of paths in the HDROP.</summary>
    uint DragQueryFileCount(nint hDrop);

    /// <summary>DragQueryFileW for one path; returns characters written, or 0 when it did not fit
    /// or the index is out of range.</summary>
    int DragQueryFileName(nint hDrop, uint index, char[] buffer);
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
    public bool GlobalUnlock(nint hMem) => GlobalUnlockNative(hMem) != 0;

    public uint RegisterClipboardFormat(string formatName) => RegisterClipboardFormatNative(formatName);

    public uint DragQueryFileCount(nint hDrop) => DragQueryFileNative(hDrop, 0xFFFFFFFFu, null, 0);
    public int DragQueryFileName(nint hDrop, uint index, char[] buffer) => (int)DragQueryFileNative(hDrop, index, buffer, (uint)buffer.Length);

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
    private static extern nint GlobalUnlockNative(nint hMem);

    [DllImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true)]
    private static extern uint RegisterClipboardFormatNative(string formatName);

    [DllImport("shell32.dll", EntryPoint = "DragQueryFileW", SetLastError = true)]
    private static extern uint DragQueryFileNative(nint dropHandle, uint fileIndex, char[]? fileName, uint bufferLength);
}
