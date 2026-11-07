using System.Windows;
using Lertaro.App.Views.QuickSwitch;
using Lertaro.Core;
using Lertaro.Core.Hook;

namespace Lertaro.App.Services.QuickSwitch;

/// <summary>
/// C1/C2: shows the Quick Switch bar once per file dialog when a suggested folder exists, and
/// hides it when the dialog goes away. The setting (default on) owns the feature; per-dialog
/// handles are remembered for the session so the bar teaches once and then leaves the user alone.
/// </summary>
internal sealed class QuickSwitchBarManager
{
    private readonly HashSet<long> _seenDialogs = new();
    private QuickSwitchBarWindow? _bar;

    public void OnExplorerActivated(IntPtr hwnd, ExplorerTracker tracker)
    {
        Hide();
        if (!UserSettings.Load().Hotkeys.QuickSwitchBarEnabled) return;
        if (!tracker.IsActiveWindowDialog) return;

        if (_seenDialogs.Contains(hwnd.ToInt64())) return;
        lock (tracker.StateLock)
        {
            if (tracker.SelectQuickSwitchPath(hwnd) is not { Length: > 0 } path) return;
            Show(hwnd, path);
        }
    }

    public void OnExplorerDeactivated() => Hide();

    private void Show(IntPtr dialogHwnd, string path)
    {
        _seenDialogs.Add(dialogHwnd.ToInt64());
        Hide();
        _bar = new QuickSwitchBarWindow(dialogHwnd, path);
        _bar.Show();
        _bar.PlaceAbove(dialogHwnd);
    }

    private void Hide()
    {
        _bar?.Close();
        _bar = null;
    }
}
