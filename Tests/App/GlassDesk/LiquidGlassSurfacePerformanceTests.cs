using System.Reflection;
using System.Windows;
using GlassDesk.UI.Controls;

namespace Lertaro.App.Tests.GlassDesk;

[TestClass]
public sealed class LiquidGlassSurfacePerformanceTests
{
    [StaTestMethod]
    public void InspectPerformance_ReportsKnownMemoryEstimatesAndVisibilityState()
    {
        using var surface = new LiquidGlassSurface();

        var visibleState = surface.InspectPerformance();
        Assert.AreEqual(0L, Read<long>(visibleState, "estimatedManagedBitmapPixelPayloadBytes"));
        Assert.AreEqual(0L, Read<long>(visibleState, "estimatedManagedWorkerArrayBytes"));
        Assert.AreEqual(0L, Read<long>(visibleState, "estimatedManagedOpticsArrayBytes"));
        Assert.AreEqual(0L, Read<long>(visibleState, "estimatedLastSourceBytes"));
        Assert.AreEqual(0L, Read<long>(visibleState, "estimatedKnownManagedPixelAndArrayPayloadBytes"));
        Assert.AreEqual(!surface.IsVisible, Read<bool>(visibleState, "hidden"));
        Assert.IsFalse(Read<bool>(visibleState, "sleeping"));

        var lastSource = typeof(LiquidGlassSurface).GetField("_lastSource", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing last source buffer.");
        lastSource.SetValue(surface, new byte[17]);
        var activeState = surface.InspectPerformance();
        Assert.AreEqual(17L, Read<long>(activeState, "estimatedLastSourceBytes"));
        Assert.AreEqual(17L, Read<long>(activeState, "estimatedKnownManagedPixelAndArrayPayloadBytes"));

        surface.SetPerformanceSleeping(true);
        var sleepingState = surface.InspectPerformance();
        Assert.IsTrue(Read<bool>(sleepingState, "sleeping"));
        Assert.AreEqual(!surface.IsVisible, Read<bool>(sleepingState, "hidden"));
    }

    [StaTestMethod]
    public void IndividuallyHiddenSurface_ReleasesAndroidMapAndRebuildsWhenShown()
    {
        var surface = new LiquidGlassSurface { IsBackdropActive = static () => false };
        var window = new TestGlassWindow
        {
            Content = surface,
            Left = -32000,
            Top = -32000,
            Width = 240,
            Height = 120
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.IsTrue(surface.IsVisible);

            RenderAndroidMap(surface);
            Assert.IsGreaterThan(0L, Read<long>(surface.InspectPerformance(), "estimatedManagedOpticsArrayBytes"));

            surface.Visibility = Visibility.Collapsed;
            window.UpdateLayout();
            Assert.IsTrue(window.IsVisible);
            Assert.IsFalse(surface.IsVisible);
            Assert.AreEqual(0L, Read<long>(surface.InspectPerformance(), "estimatedManagedOpticsArrayBytes"),
                "Hiding one surface must release its Android optical map while the owner window stays visible.");

            surface.Visibility = Visibility.Visible;
            window.UpdateLayout();
            RenderAndroidMap(surface);
            Assert.IsGreaterThan(0L, Read<long>(surface.InspectPerformance(), "estimatedManagedOpticsArrayBytes"),
                "A shown surface must be able to rebuild its renderer map.");
        }
        finally
        {
            if (window.IsVisible) window.Close();
            else surface.Dispose();
        }
    }

    [StaTestMethod]
    public void HiddenWindow_ReleasesAllRendererArraysAndRebuildsWhenShown()
    {
        var surface = new LiquidGlassSurface { IsBackdropActive = static () => false };
        var window = new TestGlassWindow
        {
            Content = surface,
            Left = -32000,
            Top = -32000,
            Width = 240,
            Height = 120
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.IsTrue(surface.IsVisible);

            PopulateRendererBuffers(surface);
            AssertRendererPayload(surface, expected: true);

            window.Hide();
            Assert.IsFalse(window.IsVisible);
            Assert.IsFalse(surface.IsVisible);
            var hiddenState = surface.InspectPerformance();
            Assert.IsTrue(Read<bool>(hiddenState, "sleeping"));
            Assert.AreEqual(0L, Read<long>(hiddenState, "estimatedManagedWorkerArrayBytes"));
            Assert.AreEqual(0L, Read<long>(hiddenState, "estimatedManagedOpticsArrayBytes"));
            Assert.AreEqual(0L, Read<long>(hiddenState, "estimatedKnownManagedPixelAndArrayPayloadBytes"));
            Assert.AreEqual(0L, ReadOwnedArrayBytes(GetSurfaceField(surface, "_optics")));
            Assert.AreEqual(0L, ReadOwnedArrayBytes(GetSurfaceField(surface, "_androidGlass")));
            AssertRendererPayload(surface, expected: false);

            window.WakeGlassSurfacesForDisplay();
            window.Show();
            window.UpdateLayout();
            Assert.IsTrue(window.IsVisible);
            Assert.IsTrue(surface.IsVisible);
            Assert.IsFalse(Read<bool>(surface.InspectPerformance(), "sleeping"));

            PopulateRendererBuffers(surface);
            AssertRendererPayload(surface, expected: true);
        }
        finally
        {
            if (window.IsLoaded) window.Close();
            else surface.Dispose();
        }
    }

    private static void PopulateRendererBuffers(LiquidGlassSurface surface)
    {
        const int width = 8;
        const int height = 8;
        var source = new byte[width * height * 4];

        var optics = (LiquidGlassOptics)GetSurfaceField(surface, "_optics");
        optics.Render(source, width, height, new Size(width, height), 2);

        RenderAndroidMap(surface, source, width, height);
        PopulateWorkerBuffers(surface, source.Length);
    }

    private static void PopulateWorkerBuffers(LiquidGlassSurface surface, int length)
    {
        var worker = GetSurfaceField(surface, "_worker");
        var render = worker.GetType().GetMethod("RenderBufferedAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing glass frame worker render method.");
        var fillBuffers = new Action<byte[], byte[]>((pixels, scratch) =>
        {
            pixels[0] = 1;
            scratch[0] = 1;
        });
        var task = (Task)(render.Invoke(worker, [length, fillBuffers])
            ?? throw new AssertFailedException("Missing glass frame worker render task."));
        task.GetAwaiter().GetResult();
        var result = task.GetType().GetProperty("Result")?.GetValue(task) as IDisposable
            ?? throw new AssertFailedException("Missing glass frame worker render result.");
        result.Dispose();
    }

    private static void AssertRendererPayload(LiquidGlassSurface surface, bool expected)
    {
        var state = surface.InspectPerformance();
        var workerBytes = Read<long>(state, "estimatedManagedWorkerArrayBytes");
        var opticsBytes = Read<long>(state, "estimatedManagedOpticsArrayBytes");
        Assert.AreEqual(expected, workerBytes > 0, "The glass frame worker buffer estimate did not match the expected state.");
        Assert.AreEqual(expected, opticsBytes > 0, "The renderer array estimate did not match the expected state.");
        Assert.AreEqual(expected,
            ReadOwnedArrayBytes(GetSurfaceField(surface, "_optics")) > 0,
            "The LiquidGlassOptics buffers and map did not match the expected state.");
        Assert.AreEqual(expected,
            ReadOwnedArrayBytes(GetSurfaceField(surface, "_androidGlass")) > 0,
            "The Android renderer map did not match the expected state.");

        var optics = GetSurfaceField(surface, "_optics");
        foreach (var field in new[] { "_fineBuffer", "_broadBuffer", "_filterScratch", "_faceBuffer", "_innerBuffer", "_outerBuffer", "_map" })
            AssertArrayField(optics, field, expected);
        AssertArrayField(GetSurfaceField(surface, "_androidGlass"), "_map", expected);

        var worker = GetSurfaceField(surface, "_worker");
        AssertArrayField(worker, "_pixels", expected);
        AssertArrayField(worker, "_scratch", expected);
    }

    private static object GetSurfaceField(LiquidGlassSurface surface, string fieldName) =>
        typeof(LiquidGlassSurface).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(surface)
        ?? throw new AssertFailedException($"Missing surface field '{fieldName}'.");

    private static long ReadOwnedArrayBytes(object renderer) =>
        (long)(renderer.GetType().GetMethod("InspectOwnedArrayBytes", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(renderer, null)
            ?? throw new AssertFailedException("Missing renderer array inspection method."));

    private static void AssertArrayField(object owner, string fieldName, bool expected)
    {
        var field = owner.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException($"Missing renderer array field '{fieldName}'.");
        var array = field.GetValue(owner) as Array;
        Assert.AreEqual(expected, array is { Length: > 0 }, $"Renderer array '{fieldName}' did not match the expected state.");
    }

    private static void RenderAndroidMap(LiquidGlassSurface surface)
        => RenderAndroidMap(surface, new byte[4 * 4 * 4], 4, 4);

    private static void RenderAndroidMap(LiquidGlassSurface surface, byte[] source, int width, int height)
    {
        var renderer = GetSurfaceField(surface, "_androidGlass");
        var render = renderer.GetType().GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing Android renderer method.");
        render.Invoke(renderer,
        [
            source, width, height, new Size(width, height), new Rect(0, 0, width, height),
            1d, false, true, .5d, .25d, .5d, null, false
        ]);
    }

    private sealed class TestGlassWindow : GlassWindow { }

    private static T Read<T>(object state, string propertyName) =>
        (T)(state.GetType().GetProperty(propertyName)?.GetValue(state)
            ?? throw new AssertFailedException($"Missing performance property '{propertyName}'."));
}
