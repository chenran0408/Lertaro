using System.Windows.Threading;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.App.Services;

// Split out purely to keep InlineSearchManager under the repo's per-file line limit. This class owns
// window creation only and delegates the resulting state back to its one InlineSearchManager owner.
internal sealed class InlineSearchWindowCreationSupport
{
    private readonly InlineSearchManager _manager;

    public InlineSearchWindowCreationSupport(InlineSearchManager manager) => _manager = manager;

    public void EnsureWindowCreated()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        PowerThrottlingHelper.WindowShowing("inline");
        // Cancels any idle trim armed by CloseInlineSearch: trimming just before a summon is strictly
        // worse than not trimming, since those pages are about to be needed again. Mirrors the quick
        // window's own WindowShowing call.
        IdleWorkingSetTrimmer.WindowShowing();
        if (_manager.Window != null)
        {
            // A newly created window may still be preparing its first display. Keep its HWND hidden and
            // leave input with the host until the preparation continuation performs the handoff.
            if (!_manager.Window.IsVisible) return;

            _manager.PrepareInputHandoff(_manager.Window);
            if (_manager.HasPendingInput)
                _manager.CompleteInputHandoff(_manager.Window.ActivateAndFocusSearchBox() ? _manager.Window : null);
            return;
        }

        var tracker = _manager.ExplorerTracker;
        var viewModel = new QuickSearchViewModel();
        viewModel.Search.UseDirectoryListingCache(_manager.DirectoryListingCache);

        // Show from the mirror immediately. IsInlineWindowOnScreen below asks the Hook's paced poller
        // for a fresh path; its normal PathCaptured event repairs this scope and re-runs the current query.
        viewModel.SearchScope = tracker.ActivePath;
        viewModel.IsInlineSearchContext = true;
        _manager.PrewarmActiveFolderListing(newSession: true);

        var window = new InlineSearchWindow(viewModel, _manager);
        _manager.Window = window;
        _manager.CurrentHostHwnd = tracker.ActiveHwnd;

        new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        window.Positioner.PositionWindowImmediate();

        _ = ShowPreparedWindowAsync(window, tracker);
        Logger.Log($"[InlineSearchManager] Created InlineSearchWindow; waiting to show. Scope: {viewModel.SearchScope}", LogLevel.Debug);
        Logger.Log($"[InlineSearchPerformance] Window creation took {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}ms before first-display preparation.", LogLevel.Debug);
    }

    private async Task ShowPreparedWindowAsync(InlineSearchWindow window, Core.Hook.ExplorerTracker tracker)
    {
        await GlassWindowPresentationHelper.ShowWhenPreparedAsync(window, () =>
        {
            window.Show();
            _manager.KeyboardHook.IsInlineSearchVisible = true;
            _manager.KeyboardHook.IsInlineWindowOnScreen = true;
            _manager.MouseHook.Start();
            window.ViewModel.EnsureServiceMonitoringActive();
            _manager.PrepareInputHandoff(window);

        var foreground = ExplorerNativeHooks.GetForegroundWindow();
        var isTextInputFocused = foreground != IntPtr.Zero && InputFocusEvaluator.IsForegroundTextInputFocused(foreground);
        if ((!isTextInputFocused || _manager.HasPendingInput) && !tracker.IsActiveWindowDialog)
        {
            if (window.ActivateAndFocusSearchBox())
            {
                _manager.KeyboardHook.IsInlineSearchVisible = false;
                _manager.KeyboardHook.Stop();
                _manager.CompleteInputHandoff(window);
            }
            else
            {
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_manager.Window != window || !window.IsVisible) return;
                    var foregroundNow = ExplorerNativeHooks.GetForegroundWindow();
                    if (foregroundNow != tracker.ActiveHwnd && foregroundNow != new System.Windows.Interop.WindowInteropHelper(window).Handle)
                    {
                        _manager.CompleteInputHandoff(null);
                        return;
                    }
                    if (window.ActivateAndFocusSearchBox())
                    {
                        _manager.KeyboardHook.IsInlineSearchVisible = false;
                        _manager.KeyboardHook.Stop();
                        _manager.CompleteInputHandoff(window);
                    }
                    else _manager.CompleteInputHandoff(null);
                }), DispatcherPriority.Input);
            }
        }
        else
        {
            var dialogHwnd = tracker.ActiveHwnd;
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (dialogHwnd == IntPtr.Zero) return;
                ExplorerNativeHooks.SetForegroundWindow(dialogHwnd);
                var editBox = ExplorerNativeHooks.FindSubEditBox(dialogHwnd);
                if (editBox != IntPtr.Zero) ExplorerNativeHooks.SetFocus(editBox);
            }), DispatcherPriority.Input);
        }

            Logger.Log($"[InlineSearchManager] Created and shown new InlineSearchWindow. Scope: {window.ViewModel.SearchScope}", LogLevel.Debug);
        }, () => ReferenceEquals(_manager.Window, window) && _manager.ExplorerTracker.ActiveHwnd == tracker.ActiveHwnd);
    }
}
