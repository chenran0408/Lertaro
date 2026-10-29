using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using GlassDesk.UI.Controls;

namespace Lertaro.App.Tests.GlassDesk;

[TestClass]
public sealed class WgcWindowCaptureReadbackTests
{
    public TestContext TestContext { get; set; } = null!;

    private delegate void CopyMappedPixels(nint data, uint rowPitch, int width, int height, byte[] pixels);

    [TestMethod]
    public void CopyMappedPixels_PreservesPackedAndPaddedRows_AndReportsReleaseTiming()
    {
        var method = typeof(LiquidGlassOptics).Assembly.GetType("WgcWindowCapture+PixelReadback", throwOnError: true)!
            .GetMethod("CopyMappedPixels", BindingFlags.Static | BindingFlags.NonPublic)!;
        var copy = method.CreateDelegate<CopyMappedPixels>();

        AssertCopyMatchesRows(copy, width: 1920, height: 1080, rowPitch: 1920 * 4);
        AssertCopyMatchesRows(copy, width: 13, height: 9, rowPitch: 64);

        const int width = 1920, height = 1080, rowPitch = width * 4, rounds = 40, batches = 5;
        var source = Marshal.AllocHGlobal(rowPitch * height);
        var optimized = new byte[rowPitch * height];
        var baseline = new byte[rowPitch * height];
        try
        {
            var bytes = new byte[rowPitch * height];
            new Random(712).NextBytes(bytes);
            Marshal.Copy(bytes, 0, source, bytes.Length);
            for (var i = 0; i < 5; i++)
            {
                CopyRows(source, rowPitch, width, height, baseline);
                copy(source, rowPitch, width, height, optimized);
            }
            CollectionAssert.AreEqual(baseline, optimized);

            var baselineMs = new double[batches];
            var optimizedMs = new double[batches];
            for (var batch = 0; batch < batches; batch++)
            {
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < rounds; i++) CopyRows(source, rowPitch, width, height, baseline);
                baselineMs[batch] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                for (var i = 0; i < rounds; i++) copy(source, rowPitch, width, height, optimized);
                optimizedMs[batch] = watch.Elapsed.TotalMilliseconds;
            }
            Array.Sort(baselineMs);
            Array.Sort(optimizedMs);
            TestContext.WriteLine($"WGC readback copy 1920x1080, {rounds} copies x {batches} batches; median row={baselineMs[batches / 2]:F2} ms, optimized={optimizedMs[batches / 2]:F2} ms, speedup={baselineMs[batches / 2] / optimizedMs[batches / 2]:F2}x; exact pixels=True");
        }
        finally
        {
            Marshal.FreeHGlobal(source);
        }
    }

    private static void AssertCopyMatchesRows(CopyMappedPixels copy, int width, int height, int rowPitch)
    {
        var source = Marshal.AllocHGlobal(rowPitch * height);
        var expected = new byte[width * height * 4];
        var actual = new byte[expected.Length];
        try
        {
            var sourceBytes = new byte[rowPitch * height];
            new Random(width + height).NextBytes(sourceBytes);
            Marshal.Copy(sourceBytes, 0, source, sourceBytes.Length);
            CopyRows(source, rowPitch, width, height, expected);
            copy(source, (uint)rowPitch, width, height, actual);
            CollectionAssert.AreEqual(expected, actual);
        }
        finally
        {
            Marshal.FreeHGlobal(source);
        }
    }

    private static void CopyRows(nint data, int rowPitch, int width, int height, byte[] pixels)
    {
        var rowBytes = width * 4;
        for (var y = 0; y < height; y++)
            Marshal.Copy(data + y * rowPitch, pixels, y * rowBytes, rowBytes);
    }
}
