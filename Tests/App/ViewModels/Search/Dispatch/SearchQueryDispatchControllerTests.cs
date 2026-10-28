using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search.Dispatch;

[TestClass]
public sealed class SearchQueryDispatchControllerTests
{
    [TestMethod]
    public async Task LatestAsyncWork_CoalescesPendingItemsAndProcessesFinalSnapshot()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new List<int>();
        var active = 0;
        var maximumActive = 0;
        var work = new LatestAsyncWork<int>(async item =>
        {
            var nowActive = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, nowActive);
            processed.Add(item);
            if (item == 1)
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
            }
            Interlocked.Decrement(ref active);
            if (item == 4)
                finished.SetResult();
        });

        work.Queue(1);
        await firstStarted.Task;
        work.Queue(2);
        work.Queue(3);
        work.Queue(4);
        releaseFirst.SetResult();
        await finished.Task;

        CollectionAssert.AreEqual(new[] { 1, 4 }, processed);
        Assert.AreEqual(1, maximumActive);
    }

    [TestMethod]
    public async Task LatestAsyncWork_ZeroIsAValidValueAndProcessorFailureDoesNotStallNextQueue()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new List<int>();
        var work = new LatestAsyncWork<int>(item =>
        {
            processed.Add(item);
            if (item == 1)
                throw new InvalidOperationException("expected test failure");
            completed.SetResult();
            return Task.CompletedTask;
        });

        work.Queue(1);
        work.Queue(0);
        await completed.Task;

        CollectionAssert.AreEqual(new[] { 1, 0 }, processed);
    }

    [TestMethod]
    public async Task LatestAsyncWork_PendingItemRunsWhenInFlightProcessorFails()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new List<int>();
        var work = new LatestAsyncWork<int>(async item =>
        {
            processed.Add(item);
            if (item == 1)
            {
                firstStarted.SetResult();
                await failFirst.Task;
                throw new InvalidOperationException("expected test failure");
            }
            secondFinished.SetResult();
        });

        work.Queue(1);
        await firstStarted.Task;
        work.Queue(2);
        failFirst.SetResult();
        await secondFinished.Task;

        CollectionAssert.AreEqual(new[] { 1, 2 }, processed);
    }

    [TestMethod]
    public async Task LatestAsyncWork_ClearPendingDropsOnlyQueuedItem()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = new List<int>();
        var work = new LatestAsyncWork<int>(async item =>
        {
            processed.Add(item);
            if (item == 1)
            {
                firstStarted.SetResult();
                await releaseFirst.Task;
                firstFinished.SetResult();
            }
            else if (item == 3)
                nextFinished.SetResult();
        });

        work.Queue(1);
        await firstStarted.Task;
        work.Queue(2);
        work.ClearPending();
        releaseFirst.SetResult();
        await firstFinished.Task;
        work.Queue(3);
        await nextFinished.Task;

        CollectionAssert.AreEqual(new[] { 1, 3 }, processed);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_CurrentQuery_PreservesBatchesAndProviderIdentity()
    {
        var first = new TestProvider("first", 301);
        var second = new TestProvider("second", 1);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs report", [first, second], () => true, batches.Add);

        CollectionAssert.AreEqual(new[] { 60, 240, 1, 1 }, batches.Select(b => b.Count).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(0, 301).Select(i => $"first-{i}").ToArray(),
            batches.Take(3).SelectMany(b => b).Select(r => r.Name).ToArray());
        Assert.IsTrue(batches.Take(3).SelectMany(b => b).All(r => ReferenceEquals(first, r.SourceProvider) && r.IsFullSearchFileResult));
        Assert.AreSame(second, batches[3][0].SourceProvider);
        Assert.AreEqual("second-0", batches[3][0].Name);
        Assert.IsTrue(first.Disposed);
        Assert.IsTrue(second.Disposed);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_AlreadySuperseded_DoesNotReadProvider()
    {
        var provider = new TestProvider("old", 100);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [provider], () => false, batches.Add);

        Assert.AreEqual(0, provider.Reads);
        Assert.IsEmpty(batches);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_SupersededDuringRead_StopsAndDisposesWithoutFlushing()
    {
        var current = true;
        var first = new TestProvider("old", 100) { BeforeYield = i => { if (i == 5) current = false; } };
        var second = new TestProvider("unread", 1);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [first, second], () => current, batches.Add);

        Assert.AreEqual(6, first.Reads);
        Assert.IsTrue(first.Disposed);
        Assert.AreEqual(0, second.Reads);
        Assert.IsEmpty(batches);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_SupersededAfterFirstBatch_DoesNotStartAnotherRead()
    {
        var current = true;
        var provider = new TestProvider("old", 100);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [provider], () => current,
            rows => { batches.Add(rows); current = false; });

        Assert.HasCount(60, Assert.ContainsSingle(batches));
        Assert.AreEqual(60, provider.Reads);
        Assert.IsTrue(provider.Disposed);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_SupersededAtEnd_DropsThePendingTail()
    {
        var current = true;
        var provider = new TestProvider("old", 5) { OnCompleted = () => current = false };
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [provider], () => current, batches.Add);

        Assert.AreEqual(5, provider.Reads);
        Assert.IsTrue(provider.Disposed);
        Assert.IsEmpty(batches);
    }

    [TestMethod]
    public void StreamFullSearchFileRows_SupersededWhileFlushing_DoesNotInvokeTheNextProvider()
    {
        var current = true;
        var first = new TestProvider("first", 1);
        var second = new TestProvider("unread", 1);
        var batches = new List<List<AppSearchResult>>();

        SearchQueryDispatchController.StreamFullSearchFileRows("cs a", [first, second], () => current,
            rows => { batches.Add(rows); current = false; });

        Assert.HasCount(1, Assert.ContainsSingle(batches));
        Assert.AreEqual(0, second.StreamsStarted, "A provider can start expensive work before returning its iterator.");
    }

    private sealed class TestProvider(string name, int count) : IFullSearchFileResultProvider
    {
        public string Name => name;
        public int Reads { get; private set; }
        public int StreamsStarted { get; private set; }
        public bool Disposed { get; private set; }
        public Action<int>? BeforeYield { get; init; }
        public Action? OnCompleted { get; init; }

        public IReadOnlyList<InstantResultItem> GetFileResults(string query, int limit) =>
            throw new InvalidOperationException("The streaming path must not collect the provider's entire answer.");

        public IEnumerable<InstantResultItem> GetFileResultsStreamed(string query, int limit)
        {
            StreamsStarted++;
            return Enumerate();
        }

        private IEnumerable<InstantResultItem> Enumerate()
        {
            try
            {
                for (var i = 0; i < count; i++)
                {
                    Reads++;
                    BeforeYield?.Invoke(i);
                    // An opaque icon path avoids both vector rendering and filesystem probes during mapping.
                    yield return new InstantResultItem { Title = $"{name}-{i}", ActionType = "None", IconData = "path:unused.ico" };
                }
                OnCompleted?.Invoke();
            }
            finally
            {
                Disposed = true;
            }
        }
    }
}
