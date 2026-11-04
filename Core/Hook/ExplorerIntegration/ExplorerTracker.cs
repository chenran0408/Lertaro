using System.Text;
using Lertaro.PluginSdk.Registries;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Core.Hook;

public class ExplorerTracker : IDisposable
{
    internal object StateLock { get; } = new();

    private readonly ExplorerWinEventHookSet _hookSet;
    private bool _isRunning;
    private readonly FileDialogNavigationTracker _dialogTracker = new();
    private readonly ExplorerWindowClassifier _classifier;
    private readonly ExplorerActivePathPoller _pathPoller;
    private readonly ExplorerPathSeeder _seeder;
    private readonly ExplorerAdapterCoordinator _adapters;
    // Internal state exposed to ExplorerWindowClassifier
    public string? LastPath { get; set; }
    public Func<string, string?>? PathNormalizer { get; set; }
    public IntPtr LastActiveHwnd { get; set; }
    public string? LastActiveExplorerPath => _dialogTracker.LastActiveExplorerPath;
    // Retains recently recognized file-manager windows and their folders for Quick Switch.
    private readonly ExplorerPathProviderHistory _pathProviderHistory = new();

    // Deliberately NOT cleared by Deactivate: a bounce through an unrelated window should retain the fallback.
    internal (IInlineSearchAdapter Adapter, IntPtr Hwnd)? LastPathProvider => _pathProviderHistory.LastProvider;

    // The two window facts the list above needs are native calls, and where Ctrl+G lands is not a policy to
    // leave unexercised; overriding them is what lets the tests below build the multi-window cases that the
    // one-slot version got wrong. Same shape as PathNormalizer, for the same reason.
    internal Func<IntPtr, bool> WindowIsAlive { get; set; } = ExplorerNativeHooks.IsWindow;

    internal Func<IntPtr, uint> WindowProcessId { get; set; } = hwnd =>
    {
        ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var processId);
        return processId;
    };

    internal void RememberPathProvider(IInlineSearchAdapter? adapter, IntPtr hwnd, string? path = null)
    {
        if (adapter == null || hwnd == IntPtr.Zero || !adapter.IsFileExplorer) return;
        var processId = string.IsNullOrEmpty(path) ? 0 : WindowProcessId(hwnd);
        _pathProviderHistory.Remember(adapter, hwnd, processId, path);
    }

    // Keeps a known provider's folder current without adding a window nobody has read a folder from yet.
    private void RefreshProviderPath(IntPtr hwnd, string path) => _pathProviderHistory.Refresh(hwnd, path);

    /// <summary>
    /// The folder Quick Switch should land on for a dialog: the provider window that shares the dialog's
    /// process when there is one -- a file manager hosting its own dialog is the only case where the answer is
    /// not a guess about which of several windows the user means -- then the most recently active provider
    /// window, then the last browsed folder whatever it came from.
    /// </summary>
    /// <remarks>Callers hold <see cref="StateLock"/>.</remarks>
    internal string? SelectQuickSwitchPath(IntPtr dialogHwnd, uint dialogProcessId = 0)
    {
        // Dead windows are dropped as the list is read rather than on a timer: a closed file manager must not
        // keep answering for a folder nobody is looking at any more.
        if (dialogProcessId == 0 && dialogHwnd != IntPtr.Zero) dialogProcessId = WindowProcessId(dialogHwnd);
        return _pathProviderHistory.Select(dialogProcessId, WindowIsAlive, _dialogTracker.LastActiveExplorerPath);
    }

    // Publishes the provider's folder as the last browsed path, which is what the App's mirror tracker turns
    // into LastActiveExplorerPath. An unread provider must not erase a known folder: a mirror takes any
    // non-dialog path at face value, and Quick Switch gives up on an empty one.
    internal void RaisePathProviderScope(string? path)
    {
        if (!string.IsNullOrEmpty(path)) RaisePathCaptured(path, false, false);
    }
    public string? LastActiveExplorerClassName { get; set; }
    public string? LastActiveExplorerWindowTitle { get; set; }
    public bool IsExplorerOrDesktopActive { get; set; }
    public bool IsDesktop { get; set; }
    private bool _isActiveWindowDialog;
    public bool IsActiveWindowDialog { get => _isActiveWindowDialog; set => _isActiveWindowDialog = value; }
    public bool IsActiveWindowExplorer { get; set; }
    /// <summary>The adapter that claimed the tracked window. Settable in-assembly so a test can build a dialog
    /// scenario without a real one; production only ever writes it from <see cref="RefreshActiveWindowAdapters"/>.</summary>
    public IFileDialogAdapter? ActiveAdapter { get; internal set; }
    public IInlineSearchAdapter? ActiveInlineAdapter { get; private set; }
    private IntPtr _activeHwnd;
    public IntPtr ActiveHwnd
    {
        get => _activeHwnd;
        set
        {
            _activeHwnd = value;
            _adapters.RefreshActiveWindowAdapters();
        }
    }

    /// <summary>Adapter reads/writes go through the coordinator; this is its raw handle on the
    /// tracked window, without re-deriving anything.</summary>
    internal IntPtr TrackedHwnd => _activeHwnd;
    internal void SetTrackedHwndDirectly(IntPtr hwnd) => _activeHwnd = hwnd;

    /// <summary>Adopts a full adapter answer under the state lock. A null isExplorer leaves
    /// IsActiveWindowExplorer untouched -- the zero-hwnd refresh historically kept it as it was.</summary>
    internal void AdoptAdapters(IFileDialogAdapter? dialog, bool isDialog, IInlineSearchAdapter? inline, bool? isExplorer)
    {
        lock (StateLock)
        {
            ActiveAdapter = dialog;
            _isActiveWindowDialog = isDialog;
            ActiveInlineAdapter = inline;
            if (isExplorer.HasValue) IsActiveWindowExplorer = isExplorer.Value;
        }
    }

    internal void AdoptDialogAdapter(IFileDialogAdapter? dialog)
    {
        lock (StateLock)
        {
            ActiveAdapter = dialog;
            _isActiveWindowDialog = true;
        }
    }

    public void RefreshActiveWindowAdapters() => _adapters.RefreshActiveWindowAdapters();
    public void RederiveActiveDialogAdapterIfStale() => _adapters.RederiveActiveDialogAdapterIfStale();
    public void SetActiveInlineAdapterDirectly(IInlineSearchAdapter? adapter, IntPtr hwnd)
        => _adapters.SetActiveInlineAdapterDirectly(adapter, hwnd);

    // Re-broadcasts whatever this tracker already believes is currently active, without re-deriving
    // anything -- used to bring a freshly (re)connected IPC client up to date. Needed because Start()'s
    // very first activation check runs synchronously at Hook-process startup, which routinely completes
    // before the App has finished connecting over the pipe; that one-time startup snapshot was the only
    // chance the App had to learn the true initial state, and HookIpcServer discards anything queued
    // before a connection completes (see its own comment), so silently missing it left the App's mirror
    // stuck at its all-zero/all-false defaults (e.g. IsDesktop stuck false) until the next real
    // foreground change corrected it -- see InlineSearchWindowPositioner, whose IsDesktop branch never
    // ran in that window, leaving the inline search window wherever it last happened to be.
    public void PublishCurrentState()
    {
        if (_activeHwnd == IntPtr.Zero) return;
        var windowTitle = new StringBuilder(256);
        ExplorerNativeHooks.GetWindowText(_activeHwnd, windowTitle, windowTitle.Capacity);
        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(_activeHwnd, sbClass, sbClass.Capacity);
        RaiseExplorerActivated(_activeHwnd, windowTitle.ToString(), sbClass.ToString(), IsDesktop);
        // Quick Switch's own state, which the App's mirror keeps apart from LastPath below. Without it a mirror
        // that connected after the provider was last read stayed empty -- no "jump to the open folder" row and
        // no scope -- until some unrelated path event happened to correct it. Before the dialog's own path, so
        // the ordering matches a live activation (see ExplorerWindowClassifier.TrackFileDialogWindow).
        if (!string.IsNullOrEmpty(LastActiveExplorerPath))
            RaisePathCaptured(LastActiveExplorerPath, false, false);
        if (!string.IsNullOrEmpty(LastPath))
            RaisePathCaptured(LastPath, IsDesktop, IsActiveWindowDialog);
    }
    public string? ActivePath => LastPath;
    public uint AppProcessId { get; set; }
    public event Action<IntPtr, string, string, bool>? OnExplorerActivated;
    public event Action? OnExplorerDeactivated;
    public event Action<string, bool, bool>? OnPathCaptured;
    public event Action? OnActiveWindowMoved;
    public event Action<string>? OnError;
    // ExplorerActivePathPoller calls this for the foreground window on every system-wide WinEvent it
    // receives -- any window anywhere moving, resizing or renaming -- so it goes through
    // ProcessNameResolver rather than Process.GetProcessById, which would enumerate every process on the
    // machine and leave behind a finalizable object each time.
    internal string GetProcessName(IntPtr hwnd)
    {
        ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
        return ProcessNameResolver.GetNameWithoutExtension(pid);
    }
    public void UpdateActiveWindow(IntPtr hwnd, string title, string className, bool isDesktop)
    {
        // Multi-field tracker mutations go through StateLock: the WinEvent tracker thread, the
        // keyboard hook thread and (in the App process) the IPC mirror all touch these fields, and
        // a torn combination -- new window's hwnd with the old window's dialog flag -- briefly
        // pointed Quick Switch and inline search at the wrong window.
        lock (StateLock)
        {
            ActiveHwnd = hwnd;
            IsExplorerOrDesktopActive = true;
            IsDesktop = isDesktop;
            IsActiveWindowExplorer = ActiveInlineAdapter?.IsFileExplorer ?? false;
            if (!IsActiveWindowDialog)
            {
                LastActiveExplorerClassName = className;
                LastActiveExplorerWindowTitle = title;
            }
            RaiseExplorerActivated(hwnd, title, className, isDesktop);
        }
    }
    public void DeactivateWindow() => Deactivate();
    // Re-derives full state (IsActiveWindowDialog, ActiveAdapter, dialog/path tracking, ...) for
    // whatever window is ACTUALLY foreground right now, instead of just wiping everything to "nothing
    // is active" -- see KeyboardHookService's own synchronous self-correction check, which used to call
    // DeactivateWindow() here and could clear IsActiveWindowDialog=true a few lines before Quick Switch
    // read it on the very same keystroke, if the async WinEvent hadn't caught up yet (e.g. right after a
    // "foreground became nothing" transition Explorer can produce, which carries hwnd==0 and is dropped
    // by WinEventProc, leaving ActiveHwnd stale until the next real foreground window shows up).
    public void ReclassifyActiveWindow(IntPtr hwnd) => _classifier.CheckActiveWindow(hwnd);

    // Bounded self-correction for LL hook threads: a reclassification that waits on the tracker lock
    // (e.g. the WinEvent tracker thread is stuck inside a slow plugin read) or that runs slow plugin
    // reads itself can stall the keyboard hook callback past LowLevelHooksTimeout, which gets the hook
    // silently dropped by Windows. Skip on a contended lock -- the WinEvent-based tracker remains
    // authoritative and will catch up -- and keep plugin reads to a fraction of a typical hook timeout.
    public void ReclassifyActiveWindowBounded(IntPtr hwnd)
        => _classifier.CheckActiveWindow(hwnd, lockWaitMs: 50, pluginTimeoutMs: 300);

    // Who is waiting for the tracked host's path. The poller consults this before entering the target
    // process, because entering it is not free for the target: a host that answers a path query by running a
    // script on its own UI thread loses the tooltip/preview it was in the middle of showing. Pointer
    // movement inside the host is never an answer -- see ExplorerHostReadFloor.
    public void RequestHostPathRead() => _pathPoller.RequestHostPathRead();
    public void SetInlineWindowOnScreen(bool onScreen) => _pathPoller.SetInlineWindowOnScreen(onScreen);
    public void UpdatePath(string path, bool isDesktop, bool? isDialog = null)
    {
        if (PathNormalizer != null)
            path = PathNormalizer(path) ?? string.Empty;
        lock (StateLock)
        {
            LastPath = path;
            if (Logger.IsEnabled(LogLevel.Debug)) Logger.Log($"[ExplorerTracker] UpdatePath captured path: {path} (isDesktop={isDesktop})", LogLevel.Debug);
            var pathIsDialog = isDialog ?? IsActiveWindowDialog;
            if (isDialog == true && !_isActiveWindowDialog && _activeHwnd != IntPtr.Zero)
            {
                // The process that owns the WinEvent path has claimed this window as a dialog, while this
                // process's own probe matched nothing -- a read that timed out, or a dialog still building.
                // The claim wins: the card is what the user is waiting on, and everything the adapter would
                // have answered for -- the anchor rect, the folder it feeds -- already has a fallback.
                //
                // Only while a window is actually tracked, though: a path event that arrives after a
                // deactivation carries the claim for a window this tracker has let go of, and honouring it
                // there would say "a dialog is active" with no dialog to point at.
                Logger.Log(
                    $"[ExplorerTracker] Path event reports 0x{_activeHwnd:x} as a claimed dialog; "
                    + "no adapter matched it in this process.",
                    LogLevel.Debug);
                _isActiveWindowDialog = true;
                RederiveActiveDialogAdapterIfStale();
            }

            if (!pathIsDialog) { _dialogTracker.SetLastActiveExplorerPath(path); RefreshProviderPath(_activeHwnd, path); }
            RaisePathCaptured(path, isDesktop, pathIsDialog);
        }
    }
    public void MoveActiveWindow() => OnActiveWindowMoved?.Invoke();
    public void RaiseErrorExternal(string msg) => RaiseError(msg);
    internal void RaiseExplorerActivated(IntPtr hwnd, string title, string cls, bool isDesktop) => OnExplorerActivated?.Invoke(hwnd, title, cls, isDesktop);
    internal void RaisePathCaptured(string path, bool isDesktop, bool isDialog) => OnPathCaptured?.Invoke(path, isDesktop, isDialog);
    internal void RaiseError(string msg) => OnError?.Invoke(msg);
    public ExplorerTracker() : this(ExplorerNativeHooks.GetForegroundWindow) { }

    internal ExplorerTracker(Func<IntPtr> getForeground)
    {
        _classifier = new ExplorerWindowClassifier(this, _dialogTracker);
        _pathPoller = new ExplorerActivePathPoller(this, _classifier, getForeground);
        _seeder = new ExplorerPathSeeder(this);
        _adapters = new ExplorerAdapterCoordinator(this);
        _hookSet = new ExplorerWinEventHookSet(OnWinEvent);
    }

    internal void UpdateObservedPath(IntPtr hwnd, string path)
    {
        lock (StateLock)
        {
            if (ExplorerActivePathPoller.IsObservedWindowStillActive(hwnd, ActiveHwnd))
                UpdatePath(path, IsDesktop);
        }
    }

    public void Start()
    {
        if (_isRunning) return;
        // Build the apartments the event callback hands its cross-process reads to before anything can be
        // dispatched to them: starting a thread from the WinEvent thread is making the shell wait on it.
        ExplorerStaInvoker.Start();
        if (!_hookSet.Start())
        {
            Logger.Log("[ExplorerTracker] Failed to register WinEvent hooks!", LogLevel.Error);
            return;
        }
        _isRunning = true;
        Logger.Log("[ExplorerTracker] Started.");
        _classifier.CheckActiveWindow(ExplorerNativeHooks.GetForegroundWindow());
        // The window in front right now is the App that just started, so nothing has read a file manager's
        // folder yet and the first Ctrl+G would do nothing (see ExplorerPathSeeder).
        //
        // Off this thread, and after the hooks are up: that probing is bounded per window but not free (a dozen
        // windows, each with its own read budget), and HookProcess aborts the whole hook -- no keyboard hook at
        // all -- if this thread has not finished starting within ten seconds. Seeding is best-effort by
        // definition; installing the hooks is not.
        _ = Task.Run(_seeder.SeedWhenNothingIsKnown);
    }
    public void Stop()
    {
        _hookSet.Stop();
        _isRunning = false;
        LastPath = null;
        LastActiveHwnd = IntPtr.Zero;
        IsExplorerOrDesktopActive = false;
        IsDesktop = false;
        ActiveHwnd = IntPtr.Zero;
        _dialogTracker.Clear();
        _pathProviderHistory.Clear();
        Logger.Log("[ExplorerTracker] Stopped.");
    }
    public bool TryGetActiveWindowRect(out RECT rect)
    {
        rect = default;
        if (ActiveHwnd == IntPtr.Zero) return false;
        if (ActiveAdapter != null && ActiveAdapter.GetDockBounds(ActiveHwnd, out var r1))
        {
            rect = new RECT { Left = r1.Left, Top = r1.Top, Right = r1.Right, Bottom = r1.Bottom };
            return true;
        }
        if (ActiveInlineAdapter != null && ActiveInlineAdapter.GetDockBounds(ActiveHwnd, out var r2))
        {
            rect = new RECT { Left = r2.Left, Top = r2.Top, Right = r2.Right, Bottom = r2.Bottom };
            return true;
        }
        var nativeRect = new ExplorerNativeHooks.RECT();
        if (ExplorerNativeHooks.DwmGetWindowAttribute(ActiveHwnd, ExplorerNativeHooks.DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, System.Runtime.InteropServices.Marshal.SizeOf<ExplorerNativeHooks.RECT>()) == 0 ||
            ExplorerNativeHooks.GetWindowRect(ActiveHwnd, out nativeRect))
        {
            rect = new RECT { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        return false;
    }

    /// <summary>
    /// Where the active dialog's own target field is, when that dialog's adapter can see it. False for a
    /// non-dialog window, and for a dialog whose adapter does not opt in -- see
    /// <see cref="IFileDialogAdapter.TryGetTargetFieldBounds"/>.
    /// </summary>
    public bool TryGetTargetFieldRect(out RECT rect) => ExplorerDialogBounds.TryGetTargetFieldRect(ActiveHwnd, ActiveAdapter, out rect);

    /// <summary>
    /// The active dialog's own file list, when that dialog's adapter can see it. False for a non-dialog
    /// window, and for a dialog whose adapter does not opt in -- see
    /// <see cref="IFileDialogAdapter.TryGetFileListBounds"/>.
    /// </summary>
    public bool TryGetFileListRect(out RECT rect) => ExplorerDialogBounds.TryGetFileListRect(ActiveHwnd, ActiveAdapter, out rect);

    private void OnWinEvent(uint eventType, IntPtr hwnd, int idObject)
    {
        if (!_isRunning) return;
        if (eventType == ExplorerNativeHooks.EVENT_SYSTEM_FOREGROUND)
        {
            _classifier.CheckActiveWindow(hwnd);
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_NAMECHANGE)
        {
            if (hwnd == ExplorerNativeHooks.GetForegroundWindow())
                _classifier.CheckActiveWindow(hwnd);
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_LOCATIONCHANGE)
        {
            if (hwnd == ActiveHwnd && IsActiveWindowDialog)
                OnActiveWindowMoved?.Invoke();
        }
        else if (eventType == ExplorerNativeHooks.EVENT_OBJECT_FOCUS)
        {
            var root = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
            if (root == ExplorerNativeHooks.GetForegroundWindow())
                _classifier.CheckActiveWindow(root);
        }
        _pathPoller.Poll(this, eventType, hwnd);
    }
    internal void Deactivate()
    {
        // Reentrant-safe: the classifier calls this while already holding StateLock.
        lock (StateLock)
        {
            var wasActive = IsExplorerOrDesktopActive;
            IsExplorerOrDesktopActive = IsDesktop = IsActiveWindowDialog = IsActiveWindowExplorer = false;
            ActiveHwnd = LastActiveHwnd = IntPtr.Zero;
            LastPath = null;
            if (wasActive) OnExplorerDeactivated?.Invoke();
        }
    }
    public void Dispose()
    {
        Stop();
        // Only on Dispose, not in Stop: Stop/Start is a restart, and the poller's deferred-poll timer is
        // owned for the tracker's whole life.
        _pathPoller.Dispose();
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
    public static IntPtr FindSubEditBox(IntPtr parent) => ExplorerNativeHooks.FindSubEditBox(parent);
}
