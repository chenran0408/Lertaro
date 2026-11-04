using System.Windows;

namespace Lertaro.App.Helpers.Visuals;

internal static class ColorPickerDialog
{
    internal static string? Choose(Window? owner, string color)
    {
        using var picker = new ColorDialog
        {
            FullOpen = true,
            Color = (color is { Length: 7 } && color[0] == '#' && color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0) ? ColorTranslator.FromHtml(color) : Color.Black
        };
        var nativeOwner = new NativeWindow();
        try
        {
            if (owner != null) nativeOwner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(owner).Handle);
            if (picker.ShowDialog(nativeOwner) != DialogResult.OK) return null;
            return $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        }
        finally { nativeOwner.ReleaseHandle(); }
    }
}
