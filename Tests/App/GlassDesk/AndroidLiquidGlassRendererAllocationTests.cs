using System.Reflection;
using System.Windows;
using GlassDesk.UI.Controls;

namespace Lertaro.App.Tests.GlassDesk;

[TestClass]
public sealed class AndroidLiquidGlassRendererAllocationTests
{
    private static readonly Type RendererType = typeof(LiquidGlassOptics).Assembly
        .GetType("GlassDesk.UI.Controls.AndroidLiquidGlassRenderer", throwOnError: true)!;
    private static readonly Type WorkerType = typeof(LiquidGlassOptics).Assembly
        .GetType("GlassDesk.UI.Controls.GlassFrameWorker", throwOnError: true)!;
    private static readonly MethodInfo RenderMethod = RendererType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo ClearMethod = RendererType.GetMethod("ClearCachedBuffers", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo InspectBytesMethod = RendererType.GetMethod("InspectOwnedArrayBytes", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo WorkerRenderMethod = WorkerType.GetMethod("RenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo WorkerInvalidateMethod = WorkerType.GetMethod("Invalidate", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo WorkerBusyProperty = WorkerType.GetProperty("IsBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly PropertyInfo FramePixelsProperty = WorkerType.Assembly
        .GetType("GlassDesk.UI.Controls.GlassFrameResult", throwOnError: true)!
        .GetProperty("Pixels", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [TestMethod]
    public void RepeatedRender_ReusesFilteredBuffer_AndMatchesFreshRenderer()
    {
        const int width = 48;
        const int height = 36;
        var source = CreateSource(width, height, 17);
        var renderer = CreateRenderer();

        var first = Render(renderer, source, width, height);
        var firstSnapshot = (byte[])first.Clone();
        var ownedBytes = InspectOwnedBytes(renderer);
        var second = Render(renderer, source, width, height);

        Assert.AreSame(first, second, "The output array should be reused after the previous frame has been consumed.");
        Assert.AreEqual(ownedBytes, InspectOwnedBytes(renderer), "The frame-sized filter buffer should be reused.");
        CollectionAssert.AreEqual(firstSnapshot, second, "A repeated frame must keep identical output pixels.");

        var changedSource = CreateSource(width, height, 83);
        var third = Render(renderer, changedSource, width, height);
        var fresh = Render(CreateRenderer(), changedSource, width, height);
        Assert.AreSame(second, third, "The same-size output array should remain warm across frames.");
        Assert.AreEqual(ownedBytes, InspectOwnedBytes(renderer), "The filter buffer should remain warm across same-size frames.");
        CollectionAssert.AreEqual(fresh, third, "Warm and fresh renderer output must match bit for bit.");
    }

    [TestMethod]
    public async Task Worker_HoldsResultLeaseUntilDispose_AndAllowsOutputReuseAfterCopy()
    {
        const int width = 48;
        const int height = 36;
        var renderer = CreateRenderer();
        var worker = CreateWorker();
        var source = CreateSource(width, height, 47);

        var firstResult = await RenderWithWorker(worker, () => Render(renderer, source, width, height));
        Assert.IsNotNull(firstResult);
        Assert.IsTrue(IsWorkerBusy(worker));
        var firstPixels = ResultPixels(firstResult);
        var snapshot = (byte[])firstPixels.Clone();

        var secondWasRendered = false;
        var rejected = await RenderWithWorker(worker, () =>
        {
            secondWasRendered = true;
            return Render(renderer, source, width, height);
        });
        Assert.IsNull(rejected);
        Assert.IsFalse(secondWasRendered);
        Assert.IsTrue(IsWorkerBusy(worker), "A rejected frame must not release the active result lease.");

        DisposeResult(firstResult);
        Assert.IsFalse(IsWorkerBusy(worker));
        var secondResult = await RenderWithWorker(worker, () => Render(renderer, source, width, height));
        Assert.IsNotNull(secondResult);
        Assert.IsTrue(IsWorkerBusy(worker));
        DisposeResult(firstResult);
        Assert.IsTrue(IsWorkerBusy(worker), "Disposing an older result again must not release the current frame lease.");
        var secondPixels = ResultPixels(secondResult);
        Assert.AreSame(firstPixels, secondPixels, "The renderer may reuse output only after the prior result is disposed.");
        CollectionAssert.AreEqual(snapshot, secondPixels);
        DisposeResult(secondResult);

        Clear(renderer);
        Assert.AreEqual(0L, InspectOwnedBytes(renderer));
        var rebuiltResult = await RenderWithWorker(worker, () => Render(renderer, source, width, height));
        Assert.IsNotNull(rebuiltResult);
        Assert.AreNotSame(firstPixels, ResultPixels(rebuiltResult));
        CollectionAssert.AreEqual(snapshot, ResultPixels(rebuiltResult));
        DisposeResult(rebuiltResult);
    }

    [TestMethod]
    public async Task Worker_ReleasesLeaseWhenGenerationBecomesStaleOrRenderThrows()
    {
        var worker = CreateWorker();
        using var renderStarted = new ManualResetEventSlim();
        using var continueRender = new ManualResetEventSlim();
        var staleTask = RenderWithWorker(worker, () =>
        {
            renderStarted.Set();
            continueRender.Wait();
            return [1, 2, 3];
        });
        Assert.IsTrue(renderStarted.Wait(TimeSpan.FromSeconds(5)));
        WorkerInvalidateMethod.Invoke(worker, null);
        continueRender.Set();

        var staleResult = await staleTask;
        Assert.IsNull(staleResult);
        Assert.IsFalse(IsWorkerBusy(worker));

        var renderThrew = false;
        try
        {
            await RenderWithWorker(worker, () => throw new InvalidOperationException("render failed"));
        }
        catch (InvalidOperationException)
        {
            renderThrew = true;
        }
        Assert.IsTrue(renderThrew);
        Assert.IsFalse(IsWorkerBusy(worker));
    }

    [TestMethod]
    public void ClearCachedBuffers_ReleasesFrameArrays_AndRebuildsOnNextRender()
    {
        const int width = 48;
        const int height = 36;
        var renderer = CreateRenderer();
        var source = CreateSource(width, height, 31);
        var beforeClear = Render(renderer, source, width, height);
        var snapshot = (byte[])beforeClear.Clone();
        Assert.IsTrue(InspectOwnedBytes(renderer) > source.Length);

        Clear(renderer);
        Assert.AreEqual(0L, InspectOwnedBytes(renderer));

        var afterClear = Render(renderer, source, width, height);
        Assert.AreNotSame(beforeClear, afterClear, "Cleared output storage must be rebuilt.");
        CollectionAssert.AreEqual(snapshot, afterClear);
    }

    private static object CreateRenderer() => Activator.CreateInstance(RendererType, nonPublic: true)!;
    private static object CreateWorker() => Activator.CreateInstance(WorkerType, nonPublic: true)!;

    private static byte[] Render(object renderer, byte[] source, int width, int height) =>
        (byte[])RenderMethod.Invoke(renderer,
        [source, width, height, new Size(width, height), new Rect(0, 0, width, height), 9d,
            false, true, .85d, .3d, .5d, null, false])!;

    private static void Clear(object renderer) => ClearMethod.Invoke(renderer, null);

    private static async Task<object?> RenderWithWorker(object worker, Func<byte[]> render)
    {
        var task = (Task)WorkerRenderMethod.Invoke(worker, [render])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    private static bool IsWorkerBusy(object worker) => (bool)WorkerBusyProperty.GetValue(worker)!;

    private static byte[] ResultPixels(object result) => (byte[])FramePixelsProperty.GetValue(result)!;

    private static void DisposeResult(object result) => ((IDisposable)result).Dispose();

    private static long InspectOwnedBytes(object renderer) => (long)InspectBytesMethod.Invoke(renderer, null)!;

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
