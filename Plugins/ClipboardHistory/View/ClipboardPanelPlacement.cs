using System.Runtime.InteropServices;
using System.Windows;

namespace Lertaro.Plugins.ClipboardHistory.View;

/// <summary>
/// Panel placement: the window opens at the mouse cursor (where the user's attention and the paste
/// target both are), clamped into the monitor the cursor sits on. The clamp is a pure function over
/// the work-area rectangle, so the geometry is unit-testable without a screen.
/// </summary>
internal static class ClipboardPanelPlacement
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    /// <summary>The cursor position clamped so a window of <paramref name="width"/>×<paramref name="height"/>
    /// fits on screen; falls back to screen-center when the cursor query fails.</summary>
    public static (double Left, double Top) Place(double width, double height)
    {
        if (!GetCursorPos(out var point)) return FallbackCenter(width, height);
        var area = MonitorArea(point.X, point.Y);
        return Clamp(point.X, point.Y, width, height, area.left, area.top, area.right, area.bottom);
    }

    /// <summary>The work area of the monitor containing the point (falls back to the primary work
    /// area). Win32 screen coordinates are pixels; WPF window coordinates are DIPs, so the border
    /// is converted with the system DPI scale.</summary>
    internal static (double left, double top, double right, double bottom) MonitorArea(int x, int y)
    {
        var scale = DpiScale();
        try
        {
            var handle = MonitorFromPoint(new POINT { X = x, Y = y }, MonitorDefaultToNearest);
            var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
            if (handle != nint.Zero && GetMonitorInfo(handle, ref info))
                return (info.RcWork.Left / scale, info.RcWork.Top / scale,
                    info.RcWork.Right / scale, info.RcWork.Bottom / scale);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
        {
        }
        var fallback = SystemParameters.WorkArea;
        return (fallback.Left, fallback.Top, fallback.Right, fallback.Bottom);
    }

    internal static (double Left, double Top) Clamp(
        double cursorX, double cursorY, double width, double height,
        double left, double top, double right, double bottom)
    {
        var x = Math.Clamp(cursorX, left, Math.Max(left, right - width));
        var y = Math.Clamp(cursorY, top, Math.Max(top, bottom - height));
        return (x, y);
    }

    private static (double Left, double Top) FallbackCenter(double width, double height)
    {
        var area = SystemParameters.WorkArea;
        return ((area.Left + area.Right - width) / 2, (area.Top + area.Bottom - height) / 2);
    }

    private static double DpiScale()
    {
        var source = PresentationSource.FromVisual(Application.Current?.MainWindow);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    private const uint MonitorDefaultToNearest = 1;

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
}
