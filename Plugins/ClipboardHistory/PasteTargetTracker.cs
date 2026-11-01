using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory;

// Remember the external window before any Lertaro surface takes keyboard focus.
internal sealed class PasteTargetTracker : IDisposable
{
    private readonly WinEventCallback _callback;
    private readonly nint _hook;
    private SourceWindow? _target;

    internal PasteTargetTracker()
    {
        _callback = (_, _, hwnd, _, _, _, _) => Remember(hwnd);
        Remember(GetForegroundWindow());
        _hook = SetWinEventHook(3, 3, 0, _callback, 0, 0, 2);
        if (_hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    internal SourceWindow? Target => _target is { IsAlive: true } target ? target : null;

    private void Remember(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return;
        // An unreadable external window replaces the old target too: retaining it could paste
        // into a different application after the user summoned us over a protected window.
        _target = SourceWindow.Read(hwnd);
    }

    public void Dispose() => UnhookWinEvent(_hook);

    private delegate void WinEventCallback(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}

internal sealed record SourceWindow(nint Handle, int ProcessId, long StartedAtTicks)
{
    internal static SourceWindow? Read(nint hwnd)
    {
        if (hwnd == 0) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return new SourceWindow(hwnd, (int)pid, process.StartTime.ToUniversalTime().Ticks);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    internal bool IsAlive => this == Read(Handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
