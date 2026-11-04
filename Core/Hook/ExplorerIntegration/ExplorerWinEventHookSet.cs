namespace Lertaro.Core.Hook;

/// <summary>
/// The four WinEvent hooks (foreground, name change, location change, focus) that feed the explorer
/// tracker, and nothing else: registration, unregistration and the raw callback that normalizes an
/// event into (eventType, hwnd, idObject). Split out purely to keep ExplorerTracker under the repo's
/// per-file line limit; it holds no tracking state of its own -- every event is handed straight to
/// the callback the tracker supplies.
/// </summary>
internal sealed class ExplorerWinEventHookSet
{
    private const uint WineventOutOfcontext = 0;

    private readonly ExplorerNativeHooks.WinEventDelegate _delegate;
    private readonly Action<uint, IntPtr, int> _onEvent;
    private IntPtr _hForegroundHook;
    private IntPtr _hNameChangeHook;
    private IntPtr _hLocationChangeHook;
    private IntPtr _hFocusHook;

    /// <param name="onEvent">Invoked for every raw WinEvent that passes the tracker's pre-filter.
    /// Called on the WinEvent thread; the callback must do its own dispatch.</param>
    public ExplorerWinEventHookSet(Action<uint, IntPtr, int> onEvent)
    {
        _onEvent = onEvent;
        _delegate = WinEventProc;
    }

    /// <summary>Whether all four hooks are currently registered.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Registers all four hooks. Returns false (with nothing left registered) when any of
    /// them was refused -- a partial hook set would make tracking silently half-blind.</summary>
    public bool Start()
    {
        _hForegroundHook = Set(ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND);
        _hNameChangeHook = Set(ExplorerNativeHooks.EVENT_OBJECT_NAMECHANGE);
        _hLocationChangeHook = Set(ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE);
        _hFocusHook = Set(ExplorerNativeHooks.EVENT_OBJECT_FOCUS);
        if (_hForegroundHook == IntPtr.Zero || _hNameChangeHook == IntPtr.Zero ||
            _hLocationChangeHook == IntPtr.Zero || _hFocusHook == IntPtr.Zero)
        {
            Stop();
            return false;
        }
        IsRunning = true;
        return true;
    }

    public void Stop()
    {
        if (_hForegroundHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hForegroundHook); _hForegroundHook = IntPtr.Zero; }
        if (_hNameChangeHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hNameChangeHook); _hNameChangeHook = IntPtr.Zero; }
        if (_hLocationChangeHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hLocationChangeHook); _hLocationChangeHook = IntPtr.Zero; }
        if (_hFocusHook != IntPtr.Zero) { ExplorerNativeHooks.UnhookWinEvent(_hFocusHook); _hFocusHook = IntPtr.Zero; }
        IsRunning = false;
    }

    private IntPtr Set(uint eventType) => ExplorerNativeHooks.SetWinEventHook(
        eventType, eventType, IntPtr.Zero, _delegate, 0, 0, WineventOutOfcontext);

    private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != 0) return;
        _onEvent(eventType, hwnd, idObject);
    }
}
