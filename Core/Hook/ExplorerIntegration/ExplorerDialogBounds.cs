using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Hook;

// Isolates adapter-supplied geometry validation and conversion from the native window tracker.
internal static class ExplorerDialogBounds
{
    internal static bool TryGetTargetFieldRect(IntPtr hwnd, IFileDialogAdapter? adapter, out ExplorerTracker.RECT rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || adapter == null || !adapter.TryGetTargetFieldBounds(hwnd, out var bounds))
            return false;
        if (!IsUsable(bounds)) return false;

        rect = ToRect(bounds);
        return true;
    }

    internal static bool TryGetFileListRect(IntPtr hwnd, IFileDialogAdapter? adapter, out ExplorerTracker.RECT rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || adapter == null || !adapter.TryGetFileListBounds(hwnd, out var bounds))
            return false;
        if (!IsUsable(bounds)) return false;

        rect = ToRect(bounds);
        return true;
    }

    private static bool IsUsable(AdapterRect bounds) =>
        bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;

    private static ExplorerTracker.RECT ToRect(AdapterRect bounds) =>
        new() { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
}
