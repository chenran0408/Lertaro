using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// Panel placement: the window opens at the mouse cursor (where the user's attention and the paste
/// target both are), clamped into the monitor the cursor sits on. The clamp is a pure function over
/// the work-area rectangle, so the geometry is unit-testable without a screen.
/// </summary>
internal static class ClipboardPanelPlacement
{
    internal readonly record struct Bounds(int Left, int Top, int Width, int Height, double Scale)
    {
        internal double WidthDip => Width / Scale;
        internal double HeightDip => Height / Scale;
    }

    private const uint NoZOrderNoActivate = 0x0014;

    internal readonly record struct Anchor(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    public static Anchor? ReadAnchor() => GetCursorPos(out var point) ? new Anchor(point.X, point.Y) : null;

    public static void PositionHandle(Window window, Anchor? anchor, double width, double height)
    {
        if (anchor is not { } cursor) return;
        var point = new POINT { X = cursor.X, Y = cursor.Y };
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info)) return;

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var work = info.RcWork;
        if (MonitorFromWindow(hwnd, MonitorDefaultToNearest) != monitor)
        {
            // Move the hidden HWND first so WPF processes the DPI change before its final placement.
            SetWindowPos(hwnd, nint.Zero, work.Left, work.Top, work.Right - work.Left,
                work.Bottom - work.Top, NoZOrderNoActivate);
        }

        var dpi = GetDpiForWindow(hwnd);
        var placement = FitToWorkArea(point.X, point.Y, dpi > 0 ? dpi / 96.0 : 1.0, width, height,
            (work.Left, work.Top, work.Right, work.Bottom));
        window.MinWidth = Math.Min(window.MinWidth, placement.WidthDip);
        window.MinHeight = Math.Min(window.MinHeight, placement.HeightDip);
        window.Width = placement.WidthDip;
        window.Height = placement.HeightDip;
        SetWindowPos(hwnd, nint.Zero, placement.Left, placement.Top, placement.Width, placement.Height, NoZOrderNoActivate);
    }

    internal static Bounds FitToWorkArea(int cursorPixelX, int cursorPixelY, double scale,
        double width, double height, (int left, int top, int right, int bottom) area)
    {
        var pixelWidth = Math.Min((int)Math.Round(width * scale), area.right - area.left);
        var pixelHeight = Math.Min((int)Math.Round(height * scale), area.bottom - area.top);
        var (left, top) = Clamp(cursorPixelX, cursorPixelY, pixelWidth, pixelHeight,
            area.left, area.top, area.right, area.bottom);
        return new Bounds(left, top, pixelWidth, pixelHeight, scale);
    }

    private static (int Left, int Top) Clamp(
        int cursorX, int cursorY, int width, int height, int left, int top, int right, int bottom)
    {
        var maxLeft = Math.Max(left, right - width);
        var maxTop = Math.Max(top, bottom - height);
        // Context-menu rule: the panel opens with its top-left corner at the cursor while there is
        // room down/right, and flips to open up/left when there is not -- the cursor rides the
        // panel's edge instead of being buried, and the window never straddles the work-area
        // border. When neither side fits, the clamp wins.
        var x = cursorX + width <= right || cursorX - width < left
            ? Math.Clamp(cursorX, left, maxLeft)
            : cursorX - width;
        var y = cursorY + height <= bottom || cursorY - height < top
            ? Math.Clamp(cursorY, top, maxTop)
            : cursorY - height;
        return (x, y);
    }

    private const uint MonitorDefaultToNearest = 2;
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int CbSize;
        public RECT RcMonitor;
        public RECT RcWork;
        public uint DwFlags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
