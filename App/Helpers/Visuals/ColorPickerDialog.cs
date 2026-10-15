using System.Windows;

namespace Lertaro.App.Helpers.Visuals;

internal static class ColorPickerDialog
{
    internal static string? Choose(Window? owner, string color)
    {
        using var picker = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            Color = (color is { Length: 7 } && color[0] == '#' && color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0) ? System.Drawing.ColorTranslator.FromHtml(color) : System.Drawing.Color.Black
        };
        var nativeOwner = new System.Windows.Forms.NativeWindow();
        try
        {
            if (owner != null) nativeOwner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(owner).Handle);
            if (picker.ShowDialog(nativeOwner) != System.Windows.Forms.DialogResult.OK) return null;
            return $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}";
        }
        finally { nativeOwner.ReleaseHandle(); }
    }
}
