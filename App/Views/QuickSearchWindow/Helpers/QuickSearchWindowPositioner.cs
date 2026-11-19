using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

public class QuickSearchWindowPositioner
{
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int WorkAreaInset = 8;
    // Space for the single-line status bar, including its vertical margins.
    private const double StatusBarDipHeight = 32;

    private readonly Lertaro.App.QuickSearchWindow _window;
    private readonly Func<IntPtr> _getLastActiveHwnd;

    public QuickSearchWindowPositioner(Lertaro.App.QuickSearchWindow window, Func<IntPtr> getLastActiveHwnd)
    {
        _window = window;
        _getLastActiveHwnd = getLastActiveHwnd;
    }

    public void PositionWindow()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        Screen screen;
        IntPtr targetMonitor;
        var mousePos = Control.MousePosition;

        // A visible window being explicitly repositioned stays on its current monitor.
        // A new summon targets the monitor under the pointer.
        if (_window.IsVisible && hwnd != IntPtr.Zero)
        {
            screen = Screen.FromHandle(hwnd);
            targetMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            screen = Screen.FromPoint(mousePos);
            targetMonitor = MonitorFromPoint(new POINT { X = mousePos.X, Y = mousePos.Y }, MONITOR_DEFAULTTONEAREST);
        }

        var (targetDpiScaleX, targetDpiScaleY) = GetMonitorDpiScale(targetMonitor);
        var wa = screen.WorkingArea;
        var settings = UserSettings.Load().SearchWindow;
        var (windowWidth, expandedHeight) = FitViewport(settings,
            (wa.Width - 2 * WorkAreaInset) / targetDpiScaleX,
            (wa.Height - 2 * WorkAreaInset) / targetDpiScaleY);

        var (relLeft, relTop) = settings.FollowCursorOnSummon
            ? ((double?)null, (double?)null)
            : (settings.RelativeLeft, settings.RelativeTop);

        var (targetPhysX, targetPhysY) = CalculatePhysicalPosition(
            wa.Left, wa.Top, wa.Width, wa.Height,
            windowWidth, expandedHeight, targetDpiScaleX, targetDpiScaleY,
            relLeft, relTop, mousePos.X, mousePos.Y);

        // Set WPF DIP properties first so WPF's internal logical coordinates are updated.
        // If WPF's internal DPI scale is temporarily stale (e.g. while hidden across different DPI monitors),
        // WPF's Left/Top setter may calculate an incorrect physical position; calling Win32 SetWindowPos
        // afterwards ensures the HWND is authoritatively placed at the exact target physical coordinates.
        _window.Left = targetPhysX / targetDpiScaleX;
        _window.Top = targetPhysY / targetDpiScaleY;

        if (hwnd != IntPtr.Zero)
        {
            SetWindowPos(hwnd, IntPtr.Zero,
                (int)Math.Round(targetPhysX), (int)Math.Round(targetPhysY), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    internal void FitViewportAtCurrentPosition()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (!_window.IsVisible || !GetWindowRect(hwnd, out var rect))
            return;

        // The search bar's current origin determines the work area even when an expanding
        // menu would otherwise cause more of the window to overlap a neighboring monitor.
        var origin = new System.Drawing.Point(rect.Left, rect.Top);
        var wa = Screen.FromPoint(origin).WorkingArea;
        var monitor = MonitorFromPoint(new POINT { X = origin.X, Y = origin.Y }, MONITOR_DEFAULTTONEAREST);
        var (scaleX, scaleY) = GetMonitorDpiScale(monitor);
        FitViewport(UserSettings.Load().SearchWindow,
            (wa.Width - 2 * WorkAreaInset) / scaleX,
            (wa.Bottom - rect.Top - WorkAreaInset) / scaleY);
    }

    private (double Width, double ExpandedHeight) FitViewport(
        SearchWindowSettings settings, double availableWidth, double availableHeight)
    {
        var margins = _window.SearchContentFrame.Margin;
        var horizontalMargins = margins.Left + margins.Right;
        var chromeHeight = margins.Top + margins.Bottom + settings.SearchBarHeight
            + _window.ResultsSeparator.Height + StatusBarDipHeight;
        _window.SearchContent.MaxWidth = Math.Max(0, availableWidth - horizontalMargins);

        var maxPanelHeight = Math.Min(UiMetrics.ScaledQuickSearchMaxResultHeight, Math.Max(0, availableHeight - chromeHeight));
        _window.ResultsPanelControl.MaxHeight = maxPanelHeight;
        _window.LstResults.MaxHeight = maxPanelHeight;
        _window.LaunchPanelControl.SetCurrentValue(FrameworkElement.MaxHeightProperty, maxPanelHeight);

        return (Math.Min(settings.SearchBarWidth, _window.SearchContent.MaxWidth) + horizontalMargins,
            chromeHeight + maxPanelHeight);
    }

    /// <summary>
    /// Places the expanded window at the remembered position or near the pointer, within the work area.
    /// </summary>
    internal static (double PhysX, double PhysY) CalculatePhysicalPosition(
        int waLeft, int waTop, int waWidth, int waHeight,
        double windowDipWidth, double windowDipHeight, double targetDpiScaleX, double targetDpiScaleY,
        double? relativeLeft, double? relativeTop,
        double cursorPhysX, double cursorPhysY)
    {
        var width = windowDipWidth * targetDpiScaleX;
        var height = windowDipHeight * targetDpiScaleY;
        var hasRememberedPosition = relativeLeft.HasValue && relativeTop.HasValue;
        var x = hasRememberedPosition ? waLeft + relativeLeft.GetValueOrDefault() * waWidth : cursorPhysX - width / 2;
        var y = hasRememberedPosition ? waTop + relativeTop.GetValueOrDefault() * waHeight : cursorPhysY + 24 * targetDpiScaleY;
        var left = waLeft + WorkAreaInset;
        var top = waTop + WorkAreaInset;

        return (
            Math.Clamp(x, left, Math.Max(left, waLeft + waWidth - width - WorkAreaInset)),
            Math.Clamp(y, top, Math.Max(top, waTop + waHeight - height - WorkAreaInset)));
    }

    internal static (double Left, double Top) CalculatePosition(
        int waLeft, int waTop, int waWidth, int waHeight,
        double windowDipWidth, double windowDipHeight, double targetDpiScaleX, double targetDpiScaleY,
        double? relativeLeft, double? relativeTop,
        double cursorPhysX, double cursorPhysY)
    {
        var scaleX = targetDpiScaleX > 0 ? targetDpiScaleX : 1.0;
        var scaleY = targetDpiScaleY > 0 ? targetDpiScaleY : 1.0;
        var (physX, physY) = CalculatePhysicalPosition(
            waLeft, waTop, waWidth, waHeight,
            windowDipWidth, windowDipHeight, scaleX, scaleY,
            relativeLeft, relativeTop,
            cursorPhysX, cursorPhysY);
        return (physX / scaleX, physY / scaleY);
    }

    // Wired to QuickSearchWindow's drag handler right after a drag finishes moving the window.
    // Records where it ended up as a fraction of whichever monitor it is now on. A drag is the
    // user pinning the bar down -- it also turns follow-cursor off, so the pinned spot actually
    // sticks on the next summon instead of being overridden the moment the hotkey goes again.
    public void SaveWindowPosition()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var screen = Screen.FromHandle(hwnd);
        var wa = screen.WorkingArea;
        if (wa.Width <= 0 || wa.Height <= 0)
            return;

        if (!GetWindowRect(hwnd, out var rect))
            return;

        var settings = UserSettings.Load();
        settings.SearchWindow.RelativeLeft = (double)(rect.Left - wa.Left) / wa.Width;
        settings.SearchWindow.RelativeTop = (double)(rect.Top - wa.Top) / wa.Height;
        settings.SearchWindow.FollowCursorOnSummon = false;
        settings.Save();
    }

    public void ResetPosition()
    {
        var settings = UserSettings.Load();
        settings.SearchWindow.RelativeLeft = null;
        settings.SearchWindow.RelativeTop = null;
        // Back to the factory behavior: follow the cursor until the next drag pins a new spot.
        settings.SearchWindow.FollowCursorOnSummon = true;
        settings.Save();
        PositionWindow();
    }

    private static (double x, double y) GetMonitorDpiScale(IntPtr hMonitor)
    {
        if (hMonitor != IntPtr.Zero && GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            return (dpiX / 96.0, dpiY / 96.0);
        return (1.0, 1.0);
    }
}
