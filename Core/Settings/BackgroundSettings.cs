namespace Lertaro.Core;

// Image also holds an MP4 path; retain the existing value/field names so saved pictures still load.
// Retired effects use Theme.
public enum BackgroundKind { Theme = 0, Image = 3 }

public sealed record BackgroundSettings
{
    public BackgroundKind Kind { get; init; }
    public string ImagePath { get; init; } = "";
    public double Opacity { get; init; } = .3;
    public bool FitImage { get; init; }

    public BackgroundSettings Normalize() => this with
    {
        Kind = Enum.IsDefined(Kind) ? Kind : BackgroundKind.Theme,
        ImagePath = ImagePath ?? "",
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0, 1) : .3
    };
}
