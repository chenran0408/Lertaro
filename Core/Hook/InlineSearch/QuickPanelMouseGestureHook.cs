using System.Runtime.InteropServices;

namespace Lertaro.Core.Hook.InlineSearch;

// All state and timers belong to the existing low-level mouse hook's message-loop thread.
internal sealed class QuickPanelMouseGestureHook : IDisposable
{
    private readonly Func<UserSettings> _settings;
    private readonly Func<bool> _suspended;
    private readonly Func<int, short> _getAsyncKeyState;
    private readonly Func<nint, UserSettings, QuickPanelWakeTestSession?, uint, bool> _allowed;
    private readonly Action<QuickPanelWakeButton, bool> _replay;
    private readonly Action<QuickPanelWakeButton>? _deferCompleteClick;
    private readonly QuickPanelMouseGestureDetector _press = new();
    private readonly Dictionary<string, QuickPanelMouseGestureDetector> _circles = new();
    private readonly TimerProc _timerProc;
    private UserSettings? _cachedSettings;
    private QuickPanelWakeTestSession? _cachedTest, _pressTest;
    private QuickPanelWakeBinding[] _rules = [];
    private QuickPanelWakeBinding? _pressRule;
    private bool _hasCircleRules;
    private nuint _timer;
    private nint _pressWindow, _circleWindow;
    private uint _lastMove, _lastReport;
    private string? _lastReason;
    private double _scale = 1;
    internal const nuint ReplayMarker = 0x4C525450;
    internal event Action<nint, string>? Triggered;

    internal QuickPanelMouseGestureHook(Func<UserSettings> settings, Func<bool> suspended, Func<int, short>? getAsyncKeyState = null,
        Func<nint, UserSettings, QuickPanelWakeTestSession?, uint, bool>? allowed = null,
        Action<QuickPanelWakeButton, bool>? replay = null,
        Action<QuickPanelWakeButton>? deferCompleteClick = null)
    { _settings = settings; _suspended = suspended; _getAsyncKeyState = getAsyncKeyState ?? GetAsyncKeyState; _allowed = allowed ?? Allowed; _replay = replay ?? Replay; _deferCompleteClick = deferCompleteClick; _timerProc = TimerTick; }

    internal bool Process(int message, int x, int y, uint time, uint mouseData = 0)
    {
        var settings = _settings();
        var test = QuickPanelWakeTestSession.Current;
        RefreshRules(settings, test);
        var button = ButtonOf(message, mouseData);
        var isDown = message is 0x0204 or 0x0207 or 0x020B;
        var isUp = message is 0x0205 or 0x0208 or 0x020C;
        if (isUp && _pressRule is { } released && button == released.Button)
        {
            StopTimer();
            _press.Move(x, y);
            if (released.Kind == QuickPanelWakeKind.Click && _press.HasPendingHold)
            {
                if (ReferenceEquals(test, _pressTest) && RootAt(x, y) == _pressWindow
                    && _allowed(_pressWindow, settings, test, time) && !Modified() && _press.TryFireClick())
                    Fire(_pressWindow, released, test);
            }
            var action = _press.Release();
            var testing = _pressTest != null;
            if (testing && released.Kind == QuickPanelWakeKind.Hold && action == QuickPanelMouseGestureDetector.ReleaseAction.ReplayClick)
                Report(_pressTest, "Wake_TestTooShort", time);
            _pressRule = null; _pressTest = null;
            if (!testing && action == QuickPanelMouseGestureDetector.ReleaseAction.ReplayClick)
            {
                if (_deferCompleteClick != null) _deferCompleteClick(released.Button);
                else _replay(released.Button, true);
            }
            return !testing && action != QuickPanelMouseGestureDetector.ReleaseAction.PassThrough;
        }
        if (isDown && button is { } pressed)
        {
            ReturnPressToSystem(); ResetCircles();
            var rule = _rules.FirstOrDefault(r => (r.Kind is QuickPanelWakeKind.Hold or QuickPanelWakeKind.Click) && r.Button == pressed);
            if (rule == null || _press.OwnsPress) return false;
            var root = RootAt(x, y);
            // Do not query target/foreground process names or fullscreen state on the down edge.
            // A short hold is replayed as a normal click on release; policy is needed only when a
            // gesture is about to fire (on the hold timer or click release).
            if (Modified()) { Report(test, "Wake_TestModifier", time); return false; }
            _pressWindow = root; _pressRule = rule; _pressTest = test;
            _scale = Math.Max(1, GetDpiForWindow(root) / 96d);
            _press.BeginHold(x, y, time, rule.HoldMilliseconds, rule.MovementTolerance * _scale);
            if (rule.Kind == QuickPanelWakeKind.Hold)
            {
                _timer = SetTimer(0, 0, (uint)rule.HoldMilliseconds, _timerProc);
                if (_timer == 0) { _press.Release(); _pressRule = null; return false; }
            }
            Report(test, "Wake_TestReady", time);
            // Own both halves: Explorer must not retain capture after a successful hold.
            // Testing observes the original pair without intercepting it or opening the panel.
            return test == null;
        }
        if (message == 0x0200)
        {
            var pending = _press.HasPendingHold;
            _press.Move(x, y);
            if (pending && !_press.HasPendingHold) Report(_pressTest, "Wake_TestMoved", time);
            if (_press.HasPendingHold) return false;
            if (pending) ReturnPressToSystem();
            if (!_hasCircleRules) return false;
            if (unchecked(time - _lastMove) < 16) return false;
            _lastMove = time;
            if (_press.OwnsPress || Modified() || AnyButtonDown()) { ResetCircles(); return false; }
            var root = RootAt(x, y);
            if (!Allowed(root, settings, test, time)) { ResetCircles(); return false; }
            if (root != _circleWindow) { ResetCircles(); _circleWindow = root; _scale = Math.Max(1, GetDpiForWindow(root) / 96d); }
            foreach (var rule in _rules.Where(r => r.Kind == QuickPanelWakeKind.Circle))
            {
                var detector = _circles[rule.Id];
                if (detector.AddCirclePoint(x, y, time, _scale, rule.CircleTurns, rule.CircleDirection, rule.MinimumRadius, rule.MaximumRadius))
                { Fire(root, rule, test); break; }
            }
        }
        else if (message is 0x0201 or 0x020A)
        { ReturnPressToSystem(); ResetCircles(); }
        return false;
    }

    private void RefreshRules(UserSettings settings, QuickPanelWakeTestSession? test)
    {
        if (ReferenceEquals(settings, _cachedSettings) && ReferenceEquals(test, _cachedTest)) return;
        _cachedSettings = settings; _cachedTest = test;
        _rules = (test != null ? new[] { test.Request.Binding } : QuickPanelWakeBindings.Get(settings.Hotkeys))
            .Where(r => r != null && r.Enabled && QuickPanelWakeBindings.Validate(r) == null && r.Kind != QuickPanelWakeKind.Keyboard)
            .DistinctBy(r => r.Id).ToArray();
        _hasCircleRules = _rules.Any(r => r.Kind == QuickPanelWakeKind.Circle);
        _circles.Clear();
        foreach (var rule in _rules.Where(r => r.Kind == QuickPanelWakeKind.Circle)) _circles[rule.Id] = new();
        _circleWindow = 0;
        if (_press.HasPendingHold) _press.CancelPendingHold();
    }

    private void TimerTick(nint hwnd, uint message, nuint id, uint time)
    {
        try
        {
            if (id != _timer) return;
            StopTimer();
            var test = QuickPanelWakeTestSession.Current;
            var settings = _settings();
            if (_pressRule is not { } rule || !ReferenceEquals(test, _pressTest)
                || !ReferenceEquals(settings, _cachedSettings) || Modified()
                || !GetCursorPos(out var point) || RootAt(point.X, point.Y) != _pressWindow
                || !_allowed(_pressWindow, settings, test, time))
            { _press.CancelPendingHold(); return; }
            _press.Move(point.X, point.Y);
            if (_press.TryFireHold(time)) Fire(_pressWindow, rule, test);
        }
        catch (Exception ex) { _press.CancelPendingHold(); Logger.Log($"[MouseWake] Long press failed: {ex.Message}", LogLevel.Warn); }
    }

    private void Fire(nint root, QuickPanelWakeBinding rule, QuickPanelWakeTestSession? test)
    {
        if (test != null) QuickPanelWakeTestSession.Report(test, "Wake_TestSuccess");
        else Triggered?.Invoke(root, rule.Id);
    }
    private void ReturnPressToSystem()
    {
        StopTimer();
        if (_press.ReturnPressToSystem() && _pressTest == null && _pressRule is { } rule) _replay(rule.Button, false);
    }
    private void ResetCircles() { foreach (var detector in _circles.Values) detector.ResetCircle(); _circleWindow = 0; }
    private void Report(QuickPanelWakeTestSession? test, string reason, uint time)
    {
        if (test == null || (_lastReason == reason && unchecked(time - _lastReport) < 1000)) return;
        _lastReport = time; _lastReason = reason; QuickPanelWakeTestSession.Report(test, reason);
    }
    private static string? ProcessOf(nint root)
    {
        ExplorerNativeHooks.GetWindowThreadProcessId(root, out var pid);
        return pid == 0 ? null : ForegroundProcessGate.GetProcessNameWithoutExtension(pid);
    }
    private bool Allowed(nint root, UserSettings settings, QuickPanelWakeTestSession? test, uint time)
    {
        var reason = root == 0 ? "Wake_TestLocation" : QuickPanelMouseWakePolicy.Reason(settings, _suspended(),
            ExplorerNativeHooks.IsDesktopWindow(root, out _), ProcessOf(root),
            ProcessOf(ExplorerNativeHooks.GetForegroundWindow()), FullscreenHelper.IsForegroundWindowFullScreen(), test?.Request);
        if (reason != null) Report(test, reason, time);
        return reason == null;
    }
    private static QuickPanelWakeButton? ButtonOf(int message, uint data) => message switch
    {
        0x0204 or 0x0205 => QuickPanelWakeButton.Right,
        0x0207 or 0x0208 => QuickPanelWakeButton.Middle,
        0x020B or 0x020C => (data >> 16) switch { 1 => QuickPanelWakeButton.Back, 2 => QuickPanelWakeButton.Forward, _ => null },
        _ => null
    };
    private bool Down(int key) => (_getAsyncKeyState(key) & 0x8000) != 0;
    private bool Modified() => Down(0x10) || Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C);
    private bool AnyButtonDown() => Down(1) || Down(2) || Down(4) || Down(5) || Down(6);
    internal static void Replay(QuickPanelWakeButton button, bool completeClick)
    {
        var down = button switch { QuickPanelWakeButton.Right => 0x8u, QuickPanelWakeButton.Middle => 0x20u, _ => 0x80u };
        var data = button switch { QuickPanelWakeButton.Back => 1u, QuickPanelWakeButton.Forward => 2u, _ => 0u };
        Input[] inputs = completeClick
            ? [new() { Mouse = new() { Flags = down, Data = data, ExtraInfo = ReplayMarker } },
               new() { Mouse = new() { Flags = down << 1, Data = data, ExtraInfo = ReplayMarker } }]
            : [new() { Mouse = new() { Flags = down, Data = data, ExtraInfo = ReplayMarker } }];
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            Logger.Log($"[MouseWake] Button replay failed: {Marshal.GetLastWin32Error()}", LogLevel.Warn);
            if (completeClick && sent == 1) SendInput(1, [inputs[1]], Marshal.SizeOf<Input>());
        }
    }
    private static nint RootAt(int x, int y) => ExplorerNativeHooks.GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), 2);
    private void StopTimer() { if (_timer != 0) { KillTimer(0, _timer); _timer = 0; } }
    public void Dispose() { StopTimer(); _press.Release(); ResetCircles(); }
    private delegate void TimerProc(nint hwnd, uint message, nuint timer, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint hwnd, nuint id, uint delay, TimerProc callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint hwnd, nuint id);
}
