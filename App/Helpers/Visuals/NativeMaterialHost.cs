using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Lertaro.App.Services.Theme;
using Lertaro.Core;

namespace Lertaro.App.Helpers.Visuals;

// Layered WPF windows do not display DWM backdrops correctly. Choose the HWND structure before creation.
internal static class NativeMaterialHost
{
    private static readonly DependencyProperty PreparedProperty = DependencyProperty.RegisterAttached("Prepared", typeof(bool), typeof(NativeMaterialHost));
    internal static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    internal static void Prepare(Window window, bool preview = false)
    {
        window.SetValue(PreparedProperty, true);
        if (IsSupported && (preview || BackgroundManager.Current.Kind == BackgroundKind.Material))
        {
            window.AllowsTransparency = false;
            window.Background = System.Windows.Media.Brushes.Transparent;
            // Layered-window shadow margins become visible glass padding in an opaque native HWND.
            if (window.Content is Border border) border.Margin = new Thickness(0);
            else if (window.Content is Grid grid && grid.Children.OfType<Border>().FirstOrDefault() is { } card)
                card.Margin = new Thickness(0);
        }
        window.SourceInitialized += (_, _) =>
        {
            if (!window.AllowsTransparency) DwmWindowCorners.ApplyRound(window);
            Refresh(window);
        };
    }

    internal static bool Refresh(Window window)
    {
        if (!IsSupported || !(bool)window.GetValue(PreparedProperty) || window.AllowsTransparency) return false;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return false;
        var settings = BackgroundManager.Current;
        var material = settings.Kind == BackgroundKind.Material;
        if (material) window.Background = System.Windows.Media.Brushes.Transparent;
        else window.SetResourceReference(Window.BackgroundProperty, "ContentBg");
        var type = material ? settings.Material == WindowMaterial.Acrylic ? 3 : 2 : 1;
        var dark = ThemeManager.Instance.ActiveTheme?.IsDark == true ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (material)
        {
            if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
                target.BackgroundColor = System.Windows.Media.Colors.Transparent;
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(handle, ref margins);
        }
        return DwmSetWindowAttribute(handle, 38, ref type, sizeof(int)) == 0 && material;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { internal int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
