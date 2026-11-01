using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using GlassDesk.UI.Controls;

namespace Lertaro.App.Tests.GlassDesk;

[TestClass]
public sealed class AndroidLiquidGlassRendererCpuOptimizationTests
{
    public TestContext TestContext { get; set; } = null!;

    private delegate byte[] RenderFrame(byte[] source, int width, int height, Size size, Rect bounds,
        double radius, bool squircle, bool dark, double strength, double tintOpacity,
        double backgroundClarity, LiquidGlassDispersionParameters? dispersion, bool launchpadOptics);

    [TestMethod]
    public void Render_320KReleaseLatencyAndPixelHash_AreReportedAcrossThreeRounds()
    {
        const int width = 800, height = 400, sampleCount = 21, roundCount = 3;
        var rendererType = typeof(LiquidGlassOptics).Assembly
            .GetType("GlassDesk.UI.Controls.AndroidLiquidGlassRenderer", throwOnError: true)!;
        var renderMethod = rendererType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var renderer = Activator.CreateInstance(rendererType, nonPublic: true)!;
        var render = renderMethod.CreateDelegate<RenderFrame>(renderer);
        var source = CreateSource(width, height, 53);
        var size = new Size(width, height);
        var bounds = new Rect(0, 0, width, height);

        for (var i = 0; i < 3; i++)
            render(source, width, height, size, bounds, 9d, false, true, .85d, .3d, .5d, null, false);

        var medianMs = new double[roundCount];
        var p95Ms = new double[roundCount];
        var allocatedBytesPerFrame = new long[roundCount];
        for (var round = 0; round < roundCount; round++)
        {
            var samples = new double[sampleCount];
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < sampleCount; i++)
            {
                var started = Stopwatch.GetTimestamp();
                render(source, width, height, size, bounds, 9d, false, true, .85d, .3d, .5d, null, false);
                samples[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            allocatedBytesPerFrame[round] = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / sampleCount;
            Array.Sort(samples);
            medianMs[round] = samples[sampleCount / 2];
            p95Ms[round] = samples[sampleCount - 2];
            var hash = Convert.ToHexString(SHA256.HashData(
                render(source, width, height, size, bounds, 9d, false, true, .85d, .3d, .5d, null, false)));
            Assert.AreEqual("13D679DDD4CB217A55694802B0C0EEAEE2BA95237BC989D7F4FCD432F04BF920", hash);
            TestContext.WriteLine($"Round {round + 1}: p50={medianMs[round]:F2} ms, p95={p95Ms[round]:F2} ms, allocated={allocatedBytesPerFrame[round]} B/frame, sha256={hash}");
        }

        Assert.IsTrue(allocatedBytesPerFrame.All(bytes => bytes == 0), "Warm Render frames must not allocate.");
    }

    private static byte[] CreateSource(int width, int height, int salt)
    {
        var source = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            source[pixel * 4] = (byte)(pixel * 17 + salt);
            source[pixel * 4 + 1] = (byte)(pixel * 29 + salt * 3);
            source[pixel * 4 + 2] = (byte)(pixel * 43 + salt * 7);
            source[pixel * 4 + 3] = 255;
        }
        return source;
    }
}
