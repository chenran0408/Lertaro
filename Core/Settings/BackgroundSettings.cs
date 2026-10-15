namespace Lertaro.Core;

public enum BackgroundKind { Theme, Solid, Gradient, Image, Material }
public enum WindowMaterial { Mica, Acrylic, LiquidGlass }
public enum GlassLevel { Frosted, LightRefraction, StrongRefraction }

public sealed record BackgroundSettings
{
    public BackgroundKind Kind { get; init; }
    public string ImagePath { get; init; } = "";
    public string FirstColor { get; init; } = "#DDEBFA";
    public string SecondColor { get; init; } = "#EADFF7";
    public double Angle { get; init; } = 135;
    public double Blur { get; init; }
    public double Tint { get; init; } = 0.65;
    public bool FitImage { get; init; }
    public WindowMaterial Material { get; init; } = WindowMaterial.LiquidGlass;
    public GlassLevel GlassLevel { get; init; } = GlassLevel.LightRefraction;

    public BackgroundSettings Normalize() => this with
    {
        Kind = Enum.IsDefined(Kind) ? Kind : BackgroundKind.Theme,
        Material = Enum.IsDefined(Material) ? Material : WindowMaterial.LiquidGlass,
        GlassLevel = Enum.IsDefined(GlassLevel) ? GlassLevel : GlassLevel.LightRefraction,
        ImagePath = ImagePath ?? "",
        FirstColor = ValidColor(FirstColor) ? FirstColor : "#DDEBFA",
        SecondColor = ValidColor(SecondColor) ? SecondColor : "#EADFF7",
        Angle = Limit(Angle, 0, 360, 135),
        Blur = Limit(Blur, 0, 30, 0),
        Tint = Limit(Tint, 0, 1, 0.65)
    };

    public static bool ValidColor(string? value) => value is { Length: 7 } && value[0] == '#'
        && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static double Limit(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
