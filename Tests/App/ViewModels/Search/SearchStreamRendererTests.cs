using Lertaro.Core;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchStreamRendererTests
{
    private static SearchResult Result(string path) => new() { Path = path };

    [TestMethod]
    public void CopySnapshot_DefaultModeCopiesTheRequestedPrefixEveryTime()
    {
        var source = new List<SearchResult> { Result("1"), Result("2"), Result("3") };
        var copiedCount = 0;

        var first = SearchStreamRenderer.CopySnapshot(source, 2, false, ref copiedCount);
        var second = SearchStreamRenderer.CopySnapshot(source, int.MaxValue, false, ref copiedCount);

        CollectionAssert.AreEqual(new[] { "1", "2" }, first.Select(result => result.Path).ToList());
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, second.Select(result => result.Path).ToList());
        Assert.AreEqual(0, copiedCount);
    }

    [TestMethod]
    public void CopySnapshot_BatchModeCopiesOnlyNewResults()
    {
        var source = new List<SearchResult> { Result("1"), Result("2"), Result("3") };
        var copiedCount = 0;

        var first = SearchStreamRenderer.CopySnapshot(source, 2, true, ref copiedCount);
        source.Add(Result("4"));
        var second = SearchStreamRenderer.CopySnapshot(source, 4, true, ref copiedCount);
        var unchanged = SearchStreamRenderer.CopySnapshot(source, int.MaxValue, true, ref copiedCount);

        CollectionAssert.AreEqual(new[] { "1", "2" }, first.Select(result => result.Path).ToList());
        CollectionAssert.AreEqual(new[] { "3", "4" }, second.Select(result => result.Path).ToList());
        Assert.IsEmpty(unchanged);
        Assert.AreEqual(4, copiedCount);
    }

    [TestMethod]
    public void DrainNewResults_ReleasesConsumedBatchesAndDeliversFinalTailOnce()
    {
        var pending = new List<SearchResult> { Result("1"), Result("2"), Result("3") };
        var head = 0;
        var copiedCount = 0;

        var first = SearchStreamRenderer.DrainNewResults(pending, 2, ref head, ref copiedCount);
        pending.Add(Result("4"));
        var second = SearchStreamRenderer.DrainNewResults(pending, 3, ref head, ref copiedCount);
        var finalTail = SearchStreamRenderer.DrainNewResults(pending, int.MaxValue, ref head, ref copiedCount);
        var unchanged = SearchStreamRenderer.DrainNewResults(pending, int.MaxValue, ref head, ref copiedCount);

        CollectionAssert.AreEqual(new[] { "1", "2" }, first.Select(result => result.Path).ToList());
        CollectionAssert.AreEqual(new[] { "3" }, second.Select(result => result.Path).ToList());
        CollectionAssert.AreEqual(new[] { "4" }, finalTail.Select(result => result.Path).ToList());
        Assert.IsEmpty(unchanged);
        Assert.IsTrue(pending.All(result => result == null), "Consumed raw results should no longer be retained by the staging list.");
        Assert.AreEqual(4, copiedCount);
    }

    [TestMethod]
    public void DrainNewResults_CompactsConsumedPrefixAndPreservesPendingTail()
    {
        var pending = Enumerable.Range(1, 8192).Select(i => Result(i.ToString())).ToList();
        var head = 0;
        var copiedCount = 0;

        var consumed = SearchStreamRenderer.DrainNewResults(pending, 4096, ref head, ref copiedCount);

        Assert.AreEqual(4096, consumed.Count);
        Assert.AreEqual(4096, copiedCount);
        Assert.AreEqual(0, head, "Once half the buffer is consumed, its remaining tail should be compacted to the front.");
        Assert.AreEqual(4096, pending.Count);
        CollectionAssert.AreEqual(
            new[] { "4097", "8192" },
            new[] { pending[0].Path, pending[^1].Path });

        var tail = SearchStreamRenderer.DrainNewResults(pending, int.MaxValue, ref head, ref copiedCount);
        Assert.AreEqual(4096, tail.Count);
        Assert.AreEqual("4097", tail[0].Path);
        Assert.AreEqual("8192", tail[^1].Path);
        Assert.AreEqual(8192, copiedCount);
    }

    [TestMethod]
    public void TakeFinalBatch_TransfersWholeListOrCopiesOnlyUnconsumedTail()
    {
        var whole = new List<SearchResult> { Result("1"), Result("2") };
        Assert.AreSame(whole, SearchStreamRenderer.TakeFinalBatch(whole, 0));

        var partiallyConsumed = new List<SearchResult> { null!, Result("3"), Result("4") };
        var tail = SearchStreamRenderer.TakeFinalBatch(partiallyConsumed, 1);

        CollectionAssert.AreEqual(new[] { "3", "4" }, tail.Select(result => result.Path).ToList());
        Assert.AreEqual(3, partiallyConsumed.Count, "Ownership transfer must not mutate the source before its owner releases it.");
    }

    [TestMethod]
    public async Task RefreshAfterAdditionalResults_WaitsForPassAndRefreshesOnlyCurrentSearch()
    {
        var additionalResultsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCount = 0;
        var refresh = SearchStreamRenderer.RefreshAfterAdditionalResultsAsync(
            additionalResultsReady.Task, CancellationToken.None, () => true,
            () => { Interlocked.Increment(ref refreshCount); return Task.CompletedTask; });

        Assert.IsFalse(refresh.IsCompleted, "The refresh must wait while the instant-result pass is still running.");
        additionalResultsReady.SetResult();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, refreshCount);

        var staleRefreshCount = 0;
        await SearchStreamRenderer.RefreshAfterAdditionalResultsAsync(
            Task.CompletedTask, CancellationToken.None, () => false,
            () => { Interlocked.Increment(ref staleRefreshCount); return Task.CompletedTask; });
        Assert.AreEqual(0, staleRefreshCount, "A late pass must not repaint a superseded query.");
    }
}
