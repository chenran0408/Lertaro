using System.Reflection;
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

    private static T Read<T>(object state, string propertyName) =>
        (T)(state.GetType().GetProperty(propertyName)?.GetValue(state)
            ?? throw new AssertFailedException($"Missing performance property '{propertyName}'."));
}
