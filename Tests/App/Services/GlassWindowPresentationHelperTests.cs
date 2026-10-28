using GlassDesk.UI.Controls;
using Lertaro.App.Services.Notifications;
using Lertaro.App.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Lertaro.App.Tests.Services;

[TestClass]
[DoNotParallelize]
public sealed class GlassWindowPresentationHelperTests
{
    [StaTestMethod]
    public void FirstShow_WaitsForPreparationBeforeShowing()
    {
        InstallDispatcherContext();
        var window = new TestGlassWindow { Left = -32000, Top = -32000, Width = 240, Height = 120 };
        var sequence = new List<string>();
        try
        {
            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                () =>
                {
                    sequence.Add("show");
                    window.Show();
                },
                isCurrent: null,
                prepareFirstDisplay: _ =>
                {
                    sequence.Add("prepare");
                    return Task.FromResult(new FirstDisplayPreparationResult(true, "test"));
                }));

            CollectionAssert.AreEqual(new[] { "prepare", "show" }, sequence);
            Assert.IsTrue(window.IsVisible);
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void HideThenShow_RunsPreparationAgainAndWakesSurfaceBeforePreparation()
    {
        InstallDispatcherContext();
        var surface = new LiquidGlassSurface();
        var window = new TestGlassWindow
        {
            Content = surface,
            Left = -32000,
            Top = -32000,
            Width = 240,
            Height = 120
        };
        var preparationCount = 0;
        Task<FirstDisplayPreparationResult> Prepare(GlassWindow candidate)
        {
            preparationCount++;
            var task = candidate.PrepareFirstDisplayAsync();
            Assert.IsFalse(Read<bool>(surface.InspectPerformance(), "sleeping"),
                "The surface must be awake before hidden-window preparation starts.");
            return task;
        }

        void Show() => Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(
            window, window.Show, isCurrent: null, prepareFirstDisplay: Prepare));

        try
        {
            Show();
            Assert.AreEqual(1, preparationCount);

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(LiquidGlassSurface).GetField("_lastSource", flags)!.SetValue(surface, new byte[23]);
            typeof(LiquidGlassSurface).GetField("_frame", flags)!.SetValue(surface,
                new WriteableBitmap(2, 2, 96, 96, PixelFormats.Pbgra32, null));
            typeof(LiquidGlassSurface).GetField("_transitionBackdrop", flags)!.SetValue(surface,
                BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null, new byte[16], 8));
            typeof(LiquidGlassSurface).GetField("_compatibilityBackdrop", flags)!.SetValue(surface,
                BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null, new byte[16], 8));
            Assert.AreEqual(48L, Read<long>(surface.InspectPerformance(), "estimatedManagedBitmapPixelPayloadBytes"));
            window.Hide();

            var hidden = surface.InspectPerformance();
            Assert.IsTrue(Read<bool>(hidden, "sleeping"));
            Assert.AreEqual(0L, Read<long>(hidden, "estimatedLastSourceBytes"));
            Assert.AreEqual(0L, Read<long>(hidden, "estimatedManagedBitmapPixelPayloadBytes"));

            Show();
            Assert.AreEqual(2, preparationCount);
            Assert.IsTrue(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [StaTestMethod]
    public void VisibleWindow_DoesNotStartPreparationAgain()
    {
        InstallDispatcherContext();
        var window = new TestGlassWindow { Left = -32000, Top = -32000, Width = 240, Height = 120 };
        var preparationCount = 0;
        try
        {
            window.Show();
            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                static () => { },
                isCurrent: null,
                prepareFirstDisplay: _ =>
                {
                    preparationCount++;
                    return Task.FromResult(new FirstDisplayPreparationResult(true, "test"));
                }));

            Assert.AreEqual(0, preparationCount);
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void StalePreparedReveal_LeavesHiddenSurfaceAsleepAndClearsBacking()
    {
        InstallDispatcherContext();
        var surface = new LiquidGlassSurface();
        var window = CreateOffscreenGlassWindow(surface);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        try
        {
            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                window.Show,
                isCurrent: static () => true,
                prepareFirstDisplay: candidate =>
                {
                    candidate.WakeGlassSurfacesForDisplay();
                    typeof(LiquidGlassSurface).GetField("_lastSource", flags)!.SetValue(surface, new byte[23]);
                    typeof(LiquidGlassSurface).GetField("_frame", flags)!.SetValue(surface,
                        new WriteableBitmap(2, 2, 96, 96, PixelFormats.Pbgra32, null));
                    return Task.FromResult(new FirstDisplayPreparationResult(true, "test"));
                }));
            window.Hide();

            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                window.Show,
                isCurrent: static () => false,
                prepareFirstDisplay: candidate =>
                {
                    candidate.WakeGlassSurfacesForDisplay();
                    typeof(LiquidGlassSurface).GetField("_lastSource", flags)!.SetValue(surface, new byte[23]);
                    typeof(LiquidGlassSurface).GetField("_frame", flags)!.SetValue(surface,
                        new WriteableBitmap(2, 2, 96, 96, PixelFormats.Pbgra32, null));
                    return Task.FromResult(new FirstDisplayPreparationResult(true, "cancelled"));
                }));

            Assert.IsFalse(window.IsVisible, "A stale reveal must not show the window.");
            var state = surface.InspectPerformance();
            Assert.IsTrue(Read<bool>(state, "sleeping"), "A cancelled hidden reveal must return the surface to deep sleep.");
            Assert.AreEqual(0L, Read<long>(state, "estimatedLastSourceBytes"));
            Assert.AreEqual(0L, Read<long>(state, "estimatedManagedBitmapPixelPayloadBytes"));
            Assert.AreEqual(0L, Read<long>(state, "estimatedKnownManagedPixelAndArrayPayloadBytes"));
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void StalePreparedReveal_DoesNotSleepWindowAlreadyShownByConcurrentOperation()
    {
        InstallDispatcherContext();
        var surface = new LiquidGlassSurface();
        var window = CreateOffscreenGlassWindow(surface);
        try
        {
            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                static () => { },
                isCurrent: static () => false,
                prepareFirstDisplay: candidate =>
                {
                    candidate.WakeGlassSurfacesForDisplay();
                    candidate.Show();
                    return Task.FromResult(new FirstDisplayPreparationResult(true, "concurrent"));
                }));

            Assert.IsTrue(window.IsVisible);
            Assert.IsFalse(Read<bool>(surface.InspectPerformance(), "sleeping"),
                "A stale callback must not sleep surfaces after another operation has shown the window.");
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void ConcurrentSummons_CurrentContinuationCanRevealSharedPreparationBeforeStaleContinuation()
    {
        InstallDispatcherContext();
        var surface = new LiquidGlassSurface();
        var window = CreateOffscreenGlassWindow(surface);
        var firstPreparation = new TaskCompletionSource<FirstDisplayPreparationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparationCount = 0;
        var staleShowCount = 0;
        var currentShowAwake = false;
        long currentShowBitmapBytes = 0;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        Task<FirstDisplayPreparationResult> Prepare(GlassWindow candidate)
        {
            preparationCount++;
            candidate.WakeGlassSurfacesForDisplay();
            typeof(LiquidGlassSurface).GetField("_lastSource", flags)!.SetValue(surface, new byte[23]);
            var side = preparationCount == 1 ? 2 : 3;
            typeof(LiquidGlassSurface).GetField("_frame", flags)!.SetValue(surface,
                new WriteableBitmap(side, side, 96, 96, PixelFormats.Pbgra32, null));
            return preparationCount == 1
                ? firstPreparation.Task
                : Task.FromResult(new FirstDisplayPreparationResult(true, "fresh"));
        }

        try
        {
            var staleSummon = GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                () => { staleShowCount++; window.Show(); },
                isCurrent: static () => false,
                prepareFirstDisplay: Prepare);
            Assert.AreEqual(1, preparationCount, "Both summons must initially share the pending preparation.");
            // Complete off-dispatcher so the stale continuation is queued. Then invoke the
            // current summon before pumping that queue; its completed await runs inline and
            // reveals the shared backing first deterministically.
            Task.Run(() => firstPreparation.SetResult(new FirstDisplayPreparationResult(true, "shared"))).GetAwaiter().GetResult();
            var currentSummon = GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                () =>
                {
                    currentShowAwake = !Read<bool>(surface.InspectPerformance(), "sleeping");
                    currentShowBitmapBytes = Read<long>(surface.InspectPerformance(), "estimatedManagedBitmapPixelPayloadBytes");
                    window.Show();
                },
                isCurrent: static () => true,
                prepareFirstDisplay: Prepare);
            Pump(Task.WhenAll(staleSummon, currentSummon));

            Assert.AreEqual(0, staleShowCount, "The superseded summon must not show the window.");
            Assert.IsTrue(currentShowAwake, "The current summon must not reveal a surface put to sleep by the stale summon.");
            Assert.AreEqual(1, preparationCount, "The current continuation must reuse and reveal the shared preparation.");
            Assert.AreEqual(16L, currentShowBitmapBytes, "The current summon must present the shared backing.");
            Assert.IsTrue(window.IsVisible);
        }
        finally
        {
            if (!firstPreparation.Task.IsCompleted)
                firstPreparation.SetResult(new FirstDisplayPreparationResult(true, "cleanup"));
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void ConcurrentSummons_StaleContinuationInvalidatesBeforeCurrentSummonPreparesFreshBacking()
    {
        InstallDispatcherContext();
        var surface = new LiquidGlassSurface();
        var window = CreateOffscreenGlassWindow(surface);
        var firstPreparation = new TaskCompletionSource<FirstDisplayPreparationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparationCount = 0;
        var staleShowCount = 0;
        var currentShowAwake = false;
        long currentShowBitmapBytes = 0;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        Task<FirstDisplayPreparationResult> Prepare(GlassWindow candidate)
        {
            preparationCount++;
            candidate.WakeGlassSurfacesForDisplay();
            typeof(LiquidGlassSurface).GetField("_lastSource", flags)!.SetValue(surface, new byte[23]);
            var side = preparationCount == 1 ? 2 : 3;
            typeof(LiquidGlassSurface).GetField("_frame", flags)!.SetValue(surface,
                new WriteableBitmap(side, side, 96, 96, PixelFormats.Pbgra32, null));
            return preparationCount == 1
                ? firstPreparation.Task
                : Task.FromResult(new FirstDisplayPreparationResult(true, "fresh"));
        }

        try
        {
            var staleSummon = GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                () => { staleShowCount++; window.Show(); },
                isCurrent: static () => false,
                prepareFirstDisplay: Prepare);
            Assert.AreEqual(1, preparationCount);

            // Completing off-dispatcher queues the stale continuation. Pump it to completion
            // before starting the current summon, so invalidation is guaranteed to win.
            Task.Run(() => firstPreparation.SetResult(new FirstDisplayPreparationResult(true, "stale"))).GetAwaiter().GetResult();
            Pump(staleSummon);

            Assert.AreEqual(0, staleShowCount, "The superseded summon must not show the window.");
            Assert.IsFalse(window.IsVisible);
            Assert.IsTrue(Read<bool>(surface.InspectPerformance(), "sleeping"),
                "The stale continuation must sleep the hidden surface before the current summon starts.");

            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                () =>
                {
                    currentShowAwake = !Read<bool>(surface.InspectPerformance(), "sleeping");
                    currentShowBitmapBytes = Read<long>(surface.InspectPerformance(), "estimatedManagedBitmapPixelPayloadBytes");
                    window.Show();
                },
                isCurrent: static () => true,
                prepareFirstDisplay: Prepare));

            Assert.AreEqual(2, preparationCount, "A stale first completion must invalidate shared backing before the current summon prepares.");
            Assert.IsTrue(currentShowAwake, "The current reveal must wake its freshly prepared surface.");
            Assert.AreEqual(36L, currentShowBitmapBytes, "The current reveal must use the complete fresh 3x3 backing.");
            Assert.IsTrue(window.IsVisible);
        }
        finally
        {
            if (!firstPreparation.Task.IsCompleted)
                firstPreparation.SetResult(new FirstDisplayPreparationResult(true, "cleanup"));
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void QuickLookDialogRestore_PreparesHiddenGlassOwnerBeforeShowing()
    {
        InstallDispatcherContext();
        var surface = new LiquidGlassSurface();
        var window = CreateOffscreenGlassWindow(surface);
        var awakeWhenVisible = false;
        window.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                awakeWhenVisible = !Read<bool>(surface.InspectPerformance(), "sleeping");
        };

        try
        {
            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(window, window.Show));
            window.Hide();
            Assert.IsTrue(Read<bool>(surface.InspectPerformance(), "sleeping"));
            awakeWhenVisible = false;

            Pump(QuickLookManager.ShowOwnerWhenPreparedAsync(window, window.Show, static () => true));

            Assert.IsTrue(window.IsVisible);
            Assert.IsTrue(awakeWhenVisible, "A QuickLook dialog restore must prepare the owner before it is shown.");
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void NotificationSessionUnlock_PreparesHiddenGlassWindowBeforeShowing()
    {
        InstallDispatcherContext();
        var surface = new LiquidGlassSurface();
        var window = CreateOffscreenGlassWindow(surface);
        var awakeWhenVisible = false;
        window.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                awakeWhenVisible = !Read<bool>(surface.InspectPerformance(), "sleeping");
        };

        try
        {
            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(window, window.Show));
            Pump(NotificationWindowManager.SetSessionWindowVisibilityAsync(window, visible: false, static () => true));
            Assert.IsTrue(Read<bool>(surface.InspectPerformance(), "sleeping"));
            awakeWhenVisible = false;

            Pump(NotificationWindowManager.SetSessionWindowVisibilityAsync(window, visible: true, static () => true));

            Assert.AreEqual(Visibility.Visible, window.Visibility);
            Assert.IsTrue(awakeWhenVisible, "Session unlock must prepare the notification surface before it is shown.");
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void HideThenShowWhilePreparationIsInFlight_ReusesTaskThenPreparesFreshly()
    {
        InstallDispatcherContext();
        var window = new TestGlassWindow { Left = -32000, Top = -32000, Width = 240, Height = 120 };
        var firstPreparation = new TaskCompletionSource<FirstDisplayPreparationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPreparation = new TaskCompletionSource<FirstDisplayPreparationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparationCount = 0;
        var showPreparationCounts = new List<int>();
        Task<FirstDisplayPreparationResult> Prepare(GlassWindow _)
        {
            preparationCount++;
            if (preparationCount == 1) return firstPreparation.Task;
            window.Dispatcher.BeginInvoke(new Action(() =>
                secondPreparation.SetResult(new FirstDisplayPreparationResult(true, "fresh"))));
            return secondPreparation.Task;
        }

        try
        {
            // Let the first summon spend its existing budget while its preparation remains in flight.
            var firstShow = GlassWindowPresentationHelper.ShowWhenPreparedAsync(window, window.Show, null, Prepare);
            Pump(firstShow);
            Assert.AreEqual(1, preparationCount);
            window.Hide();

            var secondShow = GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window, () => { showPreparationCounts.Add(preparationCount); window.Show(); }, null, Prepare);
            window.Dispatcher.BeginInvoke(new Action(() =>
                firstPreparation.SetResult(new FirstDisplayPreparationResult(true, "late"))));
            Pump(secondShow);
            Assert.AreEqual(2, preparationCount, "The fresh pass starts only after the previous preparation settles.");
            Assert.AreEqual(2, preparationCount, "The next hidden reveal must start a fresh preparation after the late task settles.");
            Assert.AreEqual(1, showPreparationCounts.Count);
            Assert.AreEqual(2, showPreparationCounts[0], "The second summon must show only after its fresh preparation starts.");
            Assert.IsTrue(window.IsVisible);
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
    }

    [StaTestMethod]
    public void HideThenShowWithStalePreparationPastBudget_ShowsAwakeWithinBudget()
    {
        InstallDispatcherContext();
        var surface = new LiquidGlassSurface();
        var window = new TestGlassWindow
        {
            Content = surface,
            Left = -32000,
            Top = -32000,
            Width = 240,
            Height = 120
        };
        var pendingPreparation = new TaskCompletionSource<FirstDisplayPreparationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparationCount = 0;
        Task<FirstDisplayPreparationResult> Prepare(GlassWindow _) 
        {
            preparationCount++;
            return pendingPreparation.Task;
        }

        try
        {
            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(window, window.Show, null, Prepare));
            window.Hide();
            Assert.IsTrue(Read<bool>(surface.InspectPerformance(), "sleeping"));

            var elapsed = Stopwatch.StartNew();
            var awakeWhenShown = false;
            Pump(GlassWindowPresentationHelper.ShowWhenPreparedAsync(
                window,
                () =>
                {
                    awakeWhenShown = !Read<bool>(surface.InspectPerformance(), "sleeping");
                    window.Show();
                },
                null,
                Prepare));
            elapsed.Stop();

            Assert.AreEqual(1, preparationCount, "The pending capture must be reused rather than overlapped.");
            Assert.IsTrue(awakeWhenShown, "Surfaces must be awake when the timed-out reveal occurs.");
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromMilliseconds(750),
                $"A repeated summon took {elapsed.Elapsed.TotalMilliseconds:0} ms despite the 250 ms budget.");
            pendingPreparation.SetResult(new FirstDisplayPreparationResult(true, "late"));
        }
        finally
        {
            if (!pendingPreparation.Task.IsCompleted)
                pendingPreparation.SetResult(new FirstDisplayPreparationResult(true, "cleanup"));
            if (window.IsVisible) window.Close();
        }
    }

    private static T Read<T>(object state, string propertyName) =>
        (T)(state.GetType().GetProperty(propertyName)?.GetValue(state)
            ?? throw new AssertFailedException($"Missing performance property '{propertyName}'."));

    private static TestGlassWindow CreateOffscreenGlassWindow(LiquidGlassSurface surface) => new()
    {
        Content = surface,
        Left = -32000,
        Top = -32000,
        Width = 240,
        Height = 120
    };

    private static Dispatcher InstallDispatcherContext()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        return dispatcher;
    }

    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(
                _ => dispatcher.BeginInvoke(() => frame.Continue = false),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }

    private sealed class TestGlassWindow : GlassWindow { }
}
