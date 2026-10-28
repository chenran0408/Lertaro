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

    private static void RenderAndroidMap(LiquidGlassSurface surface)
    {
        var rendererField = typeof(LiquidGlassSurface).GetField("_androidGlass", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing Android renderer.");
        var renderer = rendererField.GetValue(surface) ?? throw new AssertFailedException("Missing Android renderer instance.");
        var render = renderer.GetType().GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Missing Android renderer method.");
        render.Invoke(renderer,
        [
            new byte[4 * 4 * 4], 4, 4, new Size(4, 4), new Rect(0, 0, 4, 4),
            1d, false, true, .5d, .25d, .5d, null, false
        ]);
    }

    private sealed class TestGlassWindow : GlassWindow { }

    private static T Read<T>(object state, string propertyName) =>
        (T)(state.GetType().GetProperty(propertyName)?.GetValue(state)
            ?? throw new AssertFailedException($"Missing performance property '{propertyName}'."));
}
