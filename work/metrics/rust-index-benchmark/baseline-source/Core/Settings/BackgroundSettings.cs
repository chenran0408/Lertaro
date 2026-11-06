namespace Lertaro.Core;

// Image also holds an MP4 path; retain the existing value/field names so saved pictures still load.
// Retired effects use Theme.
public enum BackgroundKind { Theme = 0, Solid = 1, Gradient = 2, Image = 3, Material = 4 }

// Keep retired color-preset fields in the data contract so old settings can still be read. Solid
// and Gradient are normalized to Theme because their opaque fills cover GlassDesk's sampled glass.
public enum WindowMaterial { Mica = 0, Acrylic = 1, LiquidGlass = 2 }
public enum GlassLevel { Frosted = 0, LightRefraction = 1, StrongRefraction = 2 }

public sealed record BackgroundSettings
{
    public BackgroundKind Kind { get; init; }
    public string ImagePath { get; init; } = "";
    public double Opacity { get; init; } = .3;
    public bool FitImage { get; init; }
    public string FirstColor { get; init; } = "#DDEBFA";
    public string SecondColor { get; init; } = "#EADFF7";
    public double Angle { get; init; } = 135;
    public double Blur { get; init; }
    public double Tint { get; init; } = .65;
    public WindowMaterial Material { get; init; } = WindowMaterial.LiquidGlass;
    public GlassLevel GlassLevel { get; init; } = GlassLevel.LightRefraction;

    public BackgroundSettings Normalize() => this with
    {
        Kind = Kind is BackgroundKind.Solid or BackgroundKind.Gradient || !Enum.IsDefined(Kind)
            ? BackgroundKind.Theme : Kind,
        ImagePath = ImagePath ?? "",
        Material = Enum.IsDefined(Material) ? Material : WindowMaterial.LiquidGlass,
        GlassLevel = Enum.IsDefined(GlassLevel) ? GlassLevel : GlassLevel.LightRefraction,
        FirstColor = ValidColor(FirstColor) ? FirstColor : "#DDEBFA",
        SecondColor = ValidColor(SecondColor) ? SecondColor : "#EADFF7",
        Angle = Limit(Angle, 0, 360, 135),
        Blur = Limit(Blur, 0, 30, 0),
        Tint = Limit(Tint, 0, 1, .65),
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : .3
    };

    private static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#'
        && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static double Limit(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
