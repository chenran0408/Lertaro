using Lertaro.Core;
namespace Lertaro.App.Services.Theme;

// Persist all three choices now; activate the remaining profiles only after they are validated.
internal readonly record struct LiquidGlassProfile(double Refraction, double Blur, double EdgeWidth)
{
    internal static LiquidGlassProfile Resolve(GlassLevel requested) => new(9, 3, 30);
    internal static GlassLevel EffectiveLevel(GlassLevel requested) => GlassLevel.LightRefraction;
}
