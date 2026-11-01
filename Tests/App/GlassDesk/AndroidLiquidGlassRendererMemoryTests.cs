using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Windows;
using GlassDesk.UI.Controls;

namespace Lertaro.App.Tests.GlassDesk;

[TestClass]
public sealed class AndroidLiquidGlassRendererMemoryTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly Type RendererType = typeof(LiquidGlassOptics).Assembly
        .GetType("GlassDesk.UI.Controls.AndroidLiquidGlassRenderer", throwOnError: true)!;
    private static readonly MethodInfo RenderMethod = RendererType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo InspectOwnedBytesMethod = RendererType.GetMethod("InspectOwnedArrayBytes", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo MapField = RendererType.GetField("_map", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [TestMethod]
    public void Render_RepresentativeMaterials_MatchesBaselineCommitPixels()
    {
        var hasRecordedPixelBaseline = Avx.IsSupported && Sse41.IsSupported;
        if (!hasRecordedPixelBaseline)
            TestContext.WriteLine("Pixel hash fixtures were recorded on x64 AVX/SSE4.1; validating map size only on this architecture.");
        var mismatches = new List<string>();
        foreach (var testCase in Cases)
        {
            var renderer = Activator.CreateInstance(RendererType, nonPublic: true)!;
            var pixels = (byte[])RenderMethod.Invoke(renderer,
            [
                CreateSource(testCase.Width, testCase.Height, testCase.Pattern, testCase.Seed),
                testCase.Width, testCase.Height, testCase.Size, testCase.Bounds, testCase.Radius,
                testCase.Squircle, testCase.Dark, testCase.Strength, testCase.TintOpacity,
                testCase.Clarity, testCase.Dispersion, testCase.Launchpad
            ])!;
            var hash = Convert.ToHexString(SHA256.HashData(pixels));
            TestContext.WriteLine($"{testCase.Name}: {hash}");
            if (hasRecordedPixelBaseline && hash != BaselineHashes[testCase.Name])
                mismatches.Add($"{testCase.Name}: expected {BaselineHashes[testCase.Name]}, actual {hash}");

            var map = (Array)MapField.GetValue(renderer)!;
            var elementBytes = Marshal.SizeOf(map.GetType().GetElementType()!);
            var ownedBytes = (long)InspectOwnedBytesMethod.Invoke(renderer, null)!;
            TestContext.WriteLine($"{testCase.Name}: mapElement={elementBytes} B, mapBytes={(long)map.Length * elementBytes}, ownedBytes={ownedBytes}");
            if (elementBytes != 32)
                mismatches.Add($"{testCase.Name}: expected baseline 32-byte Sample, actual {elementBytes} bytes");
        }
        Assert.AreEqual(string.Empty, string.Join(Environment.NewLine, mismatches),
            "Renderer pixels must remain bit-identical to the 5a3355e0 baseline fixtures.");
    }

    private static readonly RenderCase[] Cases =
    [
        new("320k-baseline", 800, 400, 0, 53, new Size(800, 400), new Rect(0, 0, 800, 400), 9,
            false, true, .85, .3, .5, null, false),
        new("ramp-round-default", 64, 48, 0, 17, new Size(64, 48), new Rect(0, 0, 64, 48), 16,
            false, true, .85, .3, .5, null, false),
        new("checker-squircle-dispersion", 127, 73, 1, 41,
            new Size(158.75, 91.25), new Rect(3.5, 2.25, 120, 68), 18,
            true, false, .55, .72, .25, new LiquidGlassDispersionParameters(.008, .5, .7, .18), false),
        new("noise-launchpad", 96, 64, 2, 73, new Size(192, 128), new Rect(4, 3, 88, 58), 16,
            true, true, .92, .33, .85, new LiquidGlassDispersionParameters(.011, .5, .7, .18), true),
        new("low-intensity-inset", 48, 36, 1, 91, new Size(72, 54), new Rect(1, 1, 46, 34), 14,
            false, false, .3, .8, .6, new LiquidGlassDispersionParameters(0, .25, .4, 0), true),
    ];

    private static readonly Dictionary<string, string> BaselineHashes = new(StringComparer.Ordinal)
    {
        ["320k-baseline"] = "7047BD8144B43385BAE7E40E0DB39B3097332E50E7019F0D88E7D632EC42D7EB",
        ["ramp-round-default"] = "B6C1F67E715E6B6D6D637A7473D4546E44753362BB17BDD99CEE8F2787B793D1",
        ["checker-squircle-dispersion"] = "59F606094851407054A88AAFB51455DECF0CAED8CF1A770D020CD00F38875423",
        ["noise-launchpad"] = "F8056AD5C54E5EB26307C5E8BB4A86F41C7C3683E79E0D164D34DF7DDF8B1D59",
        ["low-intensity-inset"] = "82B0DD4703CD500EB941B8AF7F86F40D25A503FF159EAABA71F43CB9C50CD729",
    };

    private static byte[] CreateSource(int width, int height, int pattern, int seed)
    {
        var source = new byte[checked(width * height * 4)];
        var random = new Random(seed);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            switch (pattern)
            {
                case 0:
                    source[i] = (byte)(x * 17 + y * 7 + seed);
                    source[i + 1] = (byte)(x * 5 + y * 29 + seed * 3);
                    source[i + 2] = (byte)(x * 43 + y * 11 + seed * 7);
                    break;
                case 1:
                    var check = ((x / 3 + y / 2) & 1) == 0 ? 255 : 0;
                    source[i] = (byte)check;
                    source[i + 1] = (byte)(255 - check);
                    source[i + 2] = (byte)((x * 19 + y * 23 + seed) & 255);
                    break;
                default:
                    source[i] = (byte)random.Next(256);
                    source[i + 1] = (byte)random.Next(256);
                    source[i + 2] = (byte)random.Next(256);
                    break;
            }
            source[i + 3] = 255;
        }
        return source;
    }

    private sealed record RenderCase(string Name, int Width, int Height, int Pattern, int Seed,
        Size Size, Rect Bounds, double Radius, bool Squircle, bool Dark, double Strength,
        double TintOpacity, double Clarity, LiquidGlassDispersionParameters? Dispersion, bool Launchpad);
}
