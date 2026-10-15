using System.Runtime.InteropServices;

namespace Lertaro.Core.Hook.InlineSearch;

// Runs on the existing mouse hook/message-loop thread. Only a pending long press owns a native timer.
internal sealed class QuickPanelMouseGestureHook : IDisposable
{
    private readonly Func<UserSettings> _settings;
    private readonly Func<bool> _suspended;
    private readonly QuickPanelMouseGestureDetector _detector = new();
    private readonly TimerProc _timerProc;
    private nuint _timer;
    private nint _holdWindow, _circleWindow;
    private uint _lastMove;
    private double _scale = 1;
    internal const nuint ReplayMarker = 0x4C525450;
    internal event Action<nint>? Triggered;

    internal QuickPanelMouseGestureHook(Func<UserSettings> settings, Func<bool> suspended)
    {
        _settings = settings; _suspended = suspended; _timerProc = TimerTick;
    }

    internal bool Process(int message, int x, int y, uint time)
    {
        if (message == 0x0205)
        {
            StopTimer();
            var action = _detector.Release();
            if (action == QuickPanelMouseGestureDetector.ReleaseAction.ReplayClick) ReplayRightButton(true);
            return action != QuickPanelMouseGestureDetector.ReleaseAction.PassThrough;
        }
        if (message == 0x0204)
        {
            StopTimer(); _detector.Release(); _detector.ResetCircle(); _circleWindow = 0;
            var settings = _settings();
            if (!settings.Hotkeys.QuickPanelMouseRightHold || !settings.QuickPanel.Enabled || _suspended() || Modified()) return false;
            _holdWindow = RootAt(x, y);
            if (!Allowed(_holdWindow, settings)) return false;
            _scale = Math.Max(1, GetDpiForWindow(_holdWindow) / 96d);
            var delay = Math.Clamp(settings.Hotkeys.QuickPanelRightHoldMilliseconds, 300, 2000);
            _detector.BeginHold(x, y, time, delay, Math.Max(4, Math.Max(GetSystemMetrics(68), GetSystemMetrics(69))) * _scale);
            _timer = SetTimer(0, 0, (uint)delay, _timerProc);
            if (_timer == 0) { _detector.Release(); return false; }
            // Own both halves of a candidate press. Letting Explorer see DOWN while swallowing UP
            // leaves its list view holding mouse capture, even after the panel has closed.
            return true;
        }
        else if (message == 0x0200)
        {
            _detector.Move(x, y);
            if (!_detector.HasPendingHold)
            {
                StopTimer();
                if (_detector.ReturnPressToSystem()) ReplayRightButton(false);
            }
            if (unchecked(time - _lastMove) < 16) return false;
            _lastMove = time;
            var settings = _settings();
            if (!settings.Hotkeys.QuickPanelMouseCircle || _detector.OwnsPress || Modified() || AnyButtonDown()) { _detector.ResetCircle(); return false; }
            var root = RootAt(x, y);
            if (!Allowed(root, settings)) { _detector.ResetCircle(); return false; }
            if (root != _circleWindow) { _detector.ResetCircle(); _circleWindow = root; _scale = Math.Max(1, GetDpiForWindow(root) / 96d); }
            if (_detector.AddCirclePoint(x, y, time, _scale)) Triggered?.Invoke(root);
        }
        else if (message is 0x0201 or 0x0207 or 0x020A or 0x020B)
        {
            if (_detector.ReturnPressToSystem()) ReplayRightButton(false);
            _detector.CancelPendingHold(); StopTimer(); _detector.ResetCircle(); _circleWindow = 0;
        }
        return false;
    }

    private void TimerTick(nint hwnd, uint message, nuint id, uint time)
    {
        try
        {
            if (id != _timer) return;
            StopTimer();
            var settings = _settings();
            // Physical UP cancels via Process. GetAsyncKeyState cannot test a DOWN we suppressed.
            if (!settings.Hotkeys.QuickPanelMouseRightHold || Modified()
                || !GetCursorPos(out var point) || RootAt(point.X, point.Y) != _holdWindow || !Allowed(_holdWindow, settings))
            { _detector.CancelPendingHold(); return; }
            _detector.Move(point.X, point.Y);
            if (_detector.TryFireHold(time)) Triggered?.Invoke(_holdWindow);
        }
        catch (Exception ex) { _detector.CancelPendingHold(); Logger.Log($"[MouseWake] Long press failed: {ex.Message}", LogLevel.Warn); }
    }

    private static string? ProcessOf(nint root)
    {
        ExplorerNativeHooks.GetWindowThreadProcessId(root, out var pid);
        return pid == 0 ? null : ForegroundProcessGate.GetProcessNameWithoutExtension(pid);
    }
    private static bool AllowedLocation(nint root, UserSettings settings)
    {
        if (root == 0) return false;
        return ExplorerNativeHooks.IsDesktopWindow(root, out _) ? settings.Hotkeys.QuickPanelMouseAllowDesktop
            : ProcessNameFilter.Matches(ProcessOf(root), settings.Hotkeys.QuickPanelMouseAllowedProcesses);
    }
    private bool Allowed(nint root, UserSettings settings)
    {
        if (!AllowedLocation(root, settings)) return false;
        return QuickPanelMouseWakePolicy.Allows(settings, _suspended(), ExplorerNativeHooks.IsDesktopWindow(root, out _),
            ProcessOf(root), ProcessOf(ExplorerNativeHooks.GetForegroundWindow()), FullscreenHelper.IsForegroundWindowFullScreen());
    }
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static bool Modified() => Down(0x10) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C);
    private static bool AnyButtonDown() => Down(1) || Down(2) || Down(4) || Down(5) || Down(6);
    private static void ReplayRightButton(bool completeClick)
    {
        // A short click is replayed as an ordered pair; a cancelled drag restores DOWN and lets the
        // real UP through. Mark our input so it cannot start another long-press timer.
        Input[] inputs = completeClick
            ? [new() { Mouse = new() { Flags = 0x0008, ExtraInfo = ReplayMarker } }, new() { Mouse = new() { Flags = 0x0010, ExtraInfo = ReplayMarker } }]
            : [new() { Mouse = new() { Flags = 0x0008, ExtraInfo = ReplayMarker } }];
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            Logger.Log($"[MouseWake] Right-button replay failed: {Marshal.GetLastWin32Error()}", LogLevel.Warn);
            if (completeClick && sent == 1)
                SendInput(1, [inputs[1]], Marshal.SizeOf<Input>());
        }
    }
    private static nint RootAt(int x, int y) => ExplorerNativeHooks.GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), 2);
    private void StopTimer() { if (_timer != 0) { KillTimer(0, _timer); _timer = 0; } }
    public void Dispose() { StopTimer(); _detector.Release(); _detector.ResetCircle(); _holdWindow = _circleWindow = 0; }

    private delegate void TimerProc(nint hwnd, uint message, nuint timer, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint hwnd, nuint id, uint delay, TimerProc callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint hwnd, nuint id);
}
