namespace Lertaro.Plugins.QuickLookBridge.Tests;

[TestClass]
public sealed class QuickLookAvailabilityTests
{
    [TestMethod]
    public void MenuReturnsWhileProbeIsBlockedAndSharesTheCompletedResult()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var calls = 0;
        var cache = new QuickLookAvailability(() => { Interlocked.Increment(ref calls); started.Set(); release.Wait(); finished.Set(); return true; }, () => Assert.Fail());
        try
        {
            var menu = Task.Run(() => cache.Read(waitForInitial: false));
            Assert.IsTrue(menu.Wait(TimeSpan.FromSeconds(1)), "Menu must not wait for the availability probe.");
            Assert.IsFalse(menu.Result);
            Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(1)));
            Assert.IsFalse(cache.Read(waitForInitial: false));
            Assert.AreEqual(1, calls);
        }
        finally { release.Set(); }
        Assert.IsTrue(finished.Wait(TimeSpan.FromSeconds(1)));
        Assert.IsTrue(SpinWait.SpinUntil(() => cache.Read(waitForInitial: false), TimeSpan.FromSeconds(1)));
        Assert.IsTrue(cache.Read(waitForInitial: true));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void AutomaticPreviewStillGetsTheInitialProbeResult()
    {
        var calls = 0;
        var cache = new QuickLookAvailability(() => { calls++; return true; }, () => Assert.Fail());
        Assert.IsTrue(cache.Read(waitForInitial: true));
        Assert.IsTrue(cache.Read(waitForInitial: false));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task LosingAvailabilityResetsTheDockedWindow()
    {
        using var reset = new ManualResetEventSlim();
        var calls = 0;
        var cache = new QuickLookAvailability(() => Interlocked.Increment(ref calls) == 1, reset.Set);
        Assert.IsTrue(cache.Read(waitForInitial: true));
        await Task.Delay(1100);
        cache.Read(waitForInitial: false);
        Assert.IsTrue(reset.Wait(TimeSpan.FromSeconds(1)));
        Assert.IsTrue(SpinWait.SpinUntil(() => !cache.Read(waitForInitial: false), TimeSpan.FromSeconds(1)));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public void UnavailableApplicationDoesNotBecomeAMenuAction()
    {
        var cache = new QuickLookAvailability(() => false, () => Assert.Fail());
        Assert.IsFalse(cache.Read(waitForInitial: true));
        Assert.IsFalse(cache.Read(waitForInitial: false));
    }
}
