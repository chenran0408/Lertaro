using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>Focuses a stored source window, restoring it first when minimized.</summary>
internal static class SourceWindowFocus
{
    internal static void Focus(nint hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);
}
