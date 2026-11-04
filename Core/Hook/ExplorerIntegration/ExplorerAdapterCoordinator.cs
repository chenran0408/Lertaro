using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Registries;

namespace Lertaro.Core.Hook;

/// <summary>
/// The tracker's adapter half: which file-dialog and inline-search adapter owns the tracked window,
/// and the bounded re-derivation chain that repairs a dialog claimed by the wrong adapter while it
/// was still building. Split out purely to keep ExplorerTracker under the repo's per-file line
/// limit; it holds no state of its own and reads/writes the tracker under the tracker's lock.
/// </summary>
internal sealed class ExplorerAdapterCoordinator(ExplorerTracker tracker)
{
    // The gap between re-asks of a dialog this process could not measure -- ExplorerActivePathPoller's own
    // quiet period, which is the gap its equivalent retry gets.
    private const int DialogRematchGapMs = 200;

    // The window a re-derivation chain is already running for. Cleared whenever the tracked window's adapters
    // are re-derived, because Windows hands out the same handle again and a stale mark would then suppress the
    // re-ask a genuinely new dialog needs.
    private IntPtr _rederivedFor;

    /// <summary>Re-evaluates the cached adapters after settings or component enablement changes.</summary>
    public void RefreshActiveWindowAdapters()
    {
        _rederivedFor = IntPtr.Zero;
        if (tracker.TrackedHwnd == IntPtr.Zero)
        {
            tracker.AdoptAdapters(null, isDialog: false, inline: null, isExplorer: false);
            return;
        }

        var hwnd = tracker.TrackedHwnd;
        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();
        var processName = tracker.GetProcessName(hwnd);
        var budget = TimeSpan.FromMilliseconds(ExplorerWindowClassifier.DefaultPluginTimeoutMs);

        var dialog = MatchFileDialogAdapter(hwnd, className, processName, budget);
        var inline = ExplorerStaInvoker.RunOnStaWithTimeout(
            () => InlineSearchAdapterRegistry.GetMatchingAdapter(hwnd, className, processName),
            (IInlineSearchAdapter?)null, budget);
        tracker.AdoptAdapters(dialog, isDialog: dialog != null, inline: inline,
            isExplorer: !tracker.IsDesktop && (inline?.IsFileExplorer ?? false));

        // Worth its line: which adapter WON is the answer every geometry and folder-scope question is asked
        // of, and nothing else said it out loud. Three reports about where the card landed, and the two
        // adapters that divide the common dialogs on a child window built after the dialog appeared, were
        // both invisible from outside the process.
        // Asked before the message is built: this runs on every activation, and the active window changes
        // constantly. Logger.Log would discard the line either way, but only after the interpolation ran.
        if (Logger.IsEnabled(LogLevel.Debug))
            Logger.Log(
                $"[ExplorerTracker] Adapters for 0x{hwnd:x}: dialog={dialog?.GetType().Name ?? "none"}, "
                + $"inline={inline?.GetType().Name ?? "none"} (class={className}, process={processName})",
                LogLevel.Debug);
    }

    // Bounded, like every other plugin read in this file's callers. An adapter's CanHandle is a
    // cross-process probe for the dialogs whose widgets carry no window handles of their own, and for the
    // ones that walk child windows, and this runs on whichever thread just learned about a foreground
    // change -- in the App, the IPC mirror thread that carries *every* event. Measured on a Rimage folder
    // dialog: its thread was still busy initializing, this read parked behind it for seconds, and the whole
    // mirror went quiet rather than just this one window being answered wrong.
    private static IFileDialogAdapter? MatchFileDialogAdapter(IntPtr hwnd, string className, string processName, TimeSpan budget) =>
        ExplorerStaInvoker.RunOnStaWithTimeout(
            () => FileDialogAdapterRegistry.GetMatchingAdapter(hwnd, className, processName),
            (IFileDialogAdapter?)null, budget);

    /// <summary>
    /// Asks this process's adapters about the tracked dialog again, and adopts their answer if it has since
    /// become a different one.
    /// </summary>
    /// <remarks>
    /// The single read in <see cref="RefreshActiveWindowAdapters"/> happens in the instant an activation is
    /// mirrored, which is the instant a dialog is least able to answer for itself. For the common dialogs that
    /// is not merely a wrong answer, it is a DIFFERENT adapter, because two of them divide the field on a
    /// child window that has not been created yet: StandardFileDialogAdapter requires a "Breadcrumb Parent"
    /// and ClassicFileDialogAdapter -- registered ahead of it -- requires its ABSENCE, plus a file-name edit
    /// and a combo box. Both of those come from the dialog template and so exist from the first frame, while
    /// the breadcrumb is built later, so a modern dialog is claimed by the classic adapter until it settles.
    /// ClassicFileDialogAdapter has no file list to report, so the card was left with nothing to hang from
    /// and the folder-only scope that reads ActiveAdapter.TargetIsFolderOnly had nothing to read -- and
    /// because ActiveAdapter was not null, an absence check would have called this healthy.
    ///
    /// So the repair asks whether the registry's CURRENT answer differs from the one held, which is also what
    /// covers a read that simply timed out or matched nothing. Keyed on that state rather than on the path
    /// event that first reveals it, because more than one route reaches it and only one announces itself:
    /// measured on live Rimage dialogs, the tracker held an adapter it should not have for the whole life of
    /// the card without ever taking the claim branch, and only a focus change -- the next activation, hence
    /// the next RefreshActiveWindowAdapters -- ended it.
    ///
    /// Off the calling thread and speculative-tight on the read: callers include the IPC mirror thread and
    /// the geometry measurement, and parking the former is what commit 2acff94 was written to stop. The ask
    /// count and read budget are the hook process's own retry values, so both processes give the same dialog
    /// the same chance; the App mirrors state instead of polling for it, so its retry has to live here.
    ///
    /// ponytail: one bounded chain per dialog rather than a watcher, so a dialog that genuinely has no file
    /// list to report -- AutoCAD, Bandizip, WinRAR -- spends twelve cheap re-reads on it and then goes quiet.
    /// The ceiling is a dialog that finishes building only after that budget is spent; the next activation
    /// still catches it, and a card no longer visibly jumps when it does. Lifting the ceiling properly means
    /// the App polling for itself the way <see cref="ExplorerActivePathPoller"/> does, which it cannot do
    /// today because it mirrors the hook's state rather than reading events of its own.
    /// </remarks>
    public void RederiveActiveDialogAdapterIfStale()
    {
        IntPtr hwnd;
        lock (tracker.StateLock)
        {
            if (!tracker.IsActiveWindowDialog || tracker.TrackedHwnd == IntPtr.Zero) return;
            hwnd = tracker.TrackedHwnd;
            // One chain per window: the geometry probe asks on every measurement it takes, and a dialog whose
            // adapters are simply done -- AutoCAD, Bandizip, WinRAR -- must not spawn a chain per ask.
            if (_rederivedFor == hwnd) return;
            _rederivedFor = hwnd;
        }

        Task.Run(() =>
        {
            for (var ask = 1; ask <= ExplorerActivePathPoller.UnclaimedDialogRetryLimit; ask++)
            {
                if (ask > 1) Thread.Sleep(DialogRematchGapMs);

                IFileDialogAdapter? held;
                lock (tracker.StateLock)
                {
                    if (tracker.TrackedHwnd != hwnd) return;
                    held = tracker.ActiveAdapter;
                }

                var sbClass = new StringBuilder(256);
                ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
                var fresh = MatchFileDialogAdapter(hwnd, sbClass.ToString(), tracker.GetProcessName(hwnd),
                    TimeSpan.FromMilliseconds(ExplorerActivePathPoller.RetryReadBudgetMs));

                // Only a different answer is a repair. Never demote to null here: a dialog that has begun
                // torn down answers "not a file dialog" to a re-read, and adopting that would take the card
                // away from under a user who is still looking at it, while RefreshActiveWindowAdapters is
                // the right place to make that call.
                if (fresh is null || ReferenceEquals(fresh, held)) continue;

                lock (tracker.StateLock)
                {
                    // The window this was about is gone by now, so this answer belongs to nothing.
                    if (tracker.TrackedHwnd != hwnd) return;
                    tracker.AdoptDialogAdapter(fresh);
                }

                Logger.Log(
                    $"[ExplorerTracker] Dialog 0x{hwnd:x} re-derived on re-ask {ask}: "
                    + $"{held?.GetType().Name ?? "none"} -> {fresh.GetType().Name}.",
                    LogLevel.Debug);
                // The dialog can be measured now, which is the edge the card hangs from: same signal the
                // mirrored move uses, because what changed is exactly that -- an input to the placement.
                tracker.MoveActiveWindow();
                return;
            }

            Logger.Log(
                $"[ExplorerTracker] Dialog 0x{hwnd:x} still resolves to the same adapter after "
                + $"{ExplorerActivePathPoller.UnclaimedDialogRetryLimit} re-asks.",
                LogLevel.Debug);
        });
    }

    public void SetActiveInlineAdapterDirectly(IInlineSearchAdapter? adapter, IntPtr hwnd)
    {
        lock (tracker.StateLock)
        {
            tracker.AdoptAdapters(tracker.ActiveAdapter, isDialog: tracker.ActiveAdapter != null,
                inline: adapter, isExplorer: adapter != null);
            tracker.SetTrackedHwndDirectly(hwnd);
            if (adapter != null && hwnd != IntPtr.Zero)
            {
                var windowTitle = new StringBuilder(256);
                ExplorerNativeHooks.GetWindowText(hwnd, windowTitle, windowTitle.Capacity);
                var sbClass = new StringBuilder(256);
                ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
                tracker.RaiseExplorerActivated(hwnd, windowTitle.ToString(), sbClass.ToString(), false);
            }
        }
    }
}
