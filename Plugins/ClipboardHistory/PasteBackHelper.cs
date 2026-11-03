using System.ComponentModel;
using System.Runtime.InteropServices;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory;

internal static class PasteBackHelper
{
    internal static bool CanPaste(SourceWindow? target)
    {
        if (target is not { IsAlive: true })
        {
            Notify("ClipboardHistory_PasteFailed");
            return false;
        }
        if (!HasHigherIntegrity(target.ProcessId)) return true;
        Notify("ClipboardHistory_PasteElevated");
        return false;
    }

    internal static async Task PasteAsync(SourceWindow target, Func<Task> writeToClipboard)
    {
        if (!CanPaste(target)) return;
        await writeToClipboard();
        // Clipboard writes are async. Recheck identity before sending any keystrokes.
        // A different window of the same application is still the right destination.
        var foreground = SourceWindow.Read(GetForegroundWindow());
        if (!target.IsAlive ||
            (foreground is { } fg && fg.ProcessId != Environment.ProcessId && fg.ProcessId != target.ProcessId))
            throw new InvalidOperationException("Paste target changed.");
        if (GetForegroundWindow() != target.Handle && !SetForegroundWindow(target.Handle))
        {
            // Focus restoration can lose the race with the closing panel; give
            // the target one short window to become foreground before failing.
            await Task.Delay(100);
            if (GetForegroundWindow() != target.Handle) SetForegroundWindow(target.Handle);
        }
        if (GetForegroundWindow() != target.Handle)
            throw new InvalidOperationException("Paste target is not focused.");

        var inputs = BuildInputs();
        try
        {
            if (SendInput(2, inputs[..2], Marshal.SizeOf<Input>()) != 2)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            await Task.Delay(15);
        }
        finally
        {
            // Always release the injected keys, including after a partial SendInput failure.
            if (SendInput(2, inputs[2..], Marshal.SizeOf<Input>()) != 2)
                Notify("ClipboardHistory_PasteFailed");
        }
    }

    internal static Input[] BuildInputs() =>
    [
        Key(0x11, false), Key(0x56, false), Key(0x56, true), Key(0x11, true)
    ];

    private static Input Key(ushort key, bool up) => new() { Type = 1, Data = new InputUnion
        { Keyboard = new KeyboardInput { VirtualKey = key, Flags = up ? 2u : 0u } } };

    private static bool HasHigherIntegrity(int processId)
    {
        var process = OpenProcess(0x1000, false, processId);
        if (process == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { return IntegrityLevel(process) > IntegrityLevel(GetCurrentProcess()); }
        finally { CloseHandle(process); }
    }

    private static int IntegrityLevel(nint process)
    {
        if (!OpenProcessToken(process, 8, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            GetTokenInformation(token, 25, 0, 0, out var size);
            if (size == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, size, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var sid = Marshal.ReadIntPtr(buffer);
                var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }

    internal static void Notify(string key) => PluginNotificationService.Show(
        TranslationService.Get("ClipboardHistory_PluginName"), TranslationService.Get(key));

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input { internal uint Type; internal InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput { internal ushort VirtualKey, ScanCode; internal uint Flags, Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput { internal int X, Y; internal uint MouseData, Flags, Time; internal nuint ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(nint token, int informationClass, nint buffer, int length, out int required);
    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")]
    private static extern nint GetSidSubAuthority(nint sid, uint index);
}
