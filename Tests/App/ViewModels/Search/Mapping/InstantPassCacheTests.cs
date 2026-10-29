using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

/// <summary>
/// A search asks its instant providers and action plugins once. The quick and inline windows re-run
/// <see cref="SearchResultMapper.BuildQuickResults"/> on every paint of a streaming search, so without the
/// cache handed in by the dispatch, every one of those paints would wait on a fresh pass over every plugin
/// before the index matches could reach the screen.
/// </summary>
[TestClass]
public sealed class InstantPassCacheTests
{
    [TestMethod]
    public async Task EarlyEmissionAndFilePaint_ShareOneProviderCall()
    {
        var provider = new CountingProvider();
        var pass = new SearchResultMapper.InstantPassCache();
        using var ready = new CountdownEvent(2);
        using var start = new ManualResetEventSlim();
        var collectors = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            ready.Signal();
            Assert.IsTrue(start.Wait(TimeSpan.FromSeconds(15)));
            return SearchResultMapper.CollectInstantPass(pass, "md test", "test", false, @"D:\source", [provider]);
        })).ToArray();
        Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(15)));
        start.Set();
        var early = await Task.WhenAll(collectors);
        var final = SearchResultMapper.BuildQuickResults(TwoFiles(), "test", null, @"D:\source", false,
            rawQuery: "md test", instantPass: pass);

        Assert.AreEqual(1, provider.Calls);
        Assert.AreEqual("md test", provider.Query);
        Assert.AreEqual(@"D:\source", provider.Directory);
        Assert.AreSame(Assert.ContainsSingle(early[0]), Assert.ContainsSingle(early[1]));
        Assert.AreSame(early[0][0], final[0]);
        Assert.AreEqual(@"D:\source", final[0].ContextDirectory);
        Assert.HasCount(1, final.Where(row => ReferenceEquals(row.SourceProvider, provider)));
    }

    [TestMethod]
    public async Task GlobalQuickPaint_DoesNotWaitForBlockedInstantProvider_AndIncludesItAfterCompletion()
    {
        using var provider = new BlockingProvider();
        var pass = new SearchResultMapper.InstantPassCache();
        var providerPass = Task.Run(() => SearchResultMapper.CollectInstantPass(
            pass, "pcl", "pcl", false, null, [provider]));

        try
        {
            Assert.IsTrue(provider.Entered.Wait(TimeSpan.FromSeconds(5)), "The provider should be running before the file paint starts.");

            var paint = Task.Run(() => SearchResultMapper.BuildQuickResults(
                PclFiles(), "pcl", scope: null, contextDirectory: null, isInlineWindow: false,
                rawQuery: "pcl", instantPass: pass));
            var completed = await Task.WhenAny(paint, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.AreSame(paint, completed, "The global quick-search file paint must not wait for a slow instant provider.");
            var earlyRows = await paint;
            CollectionAssert.IsSubsetOf(
                new[] { @"C:\Notes\pcl-alpha.txt", @"C:\Notes\pcl-beta.txt" },
                earlyRows.Select(row => row.FullPath).ToArray());
            Assert.IsFalse(earlyRows.Any(row => ReferenceEquals(row.SourceProvider, provider)));
            Assert.IsFalse(pass.Completion.IsCompleted, "The controlled provider is still blocked at this point.");

            provider.Release.Set();
            await providerPass.WaitAsync(TimeSpan.FromSeconds(5));
            await pass.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            var refreshedRows = SearchResultMapper.BuildQuickResults(
                PclFiles(), "pcl", scope: null, contextDirectory: null, isInlineWindow: false,
                rawQuery: "pcl", instantPass: pass);
            Assert.IsTrue(refreshedRows.Any(row => ReferenceEquals(row.SourceProvider, provider) && row.Name == "Late provider result"));
            Assert.AreEqual(1, provider.Calls);
        }
        finally
        {
            provider.Release.Set();
            await providerPass.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class CountingProvider : IInstantResultProvider
    {
        public string Name => "Dictionary";
        public int Calls;
        public string? Query;
        public string? Directory;
        public IEnumerable<InstantResultItem> GetInstantResults(string query) =>
            throw new AssertFailedException("The provider needs the query's directory.");
        public IEnumerable<InstantResultItem> GetInstantResults(string query, string? contextDirectory)
        {
            Interlocked.Increment(ref Calls);
            Query = query;
            Directory = contextDirectory;
            return [new InstantResultItem { Title = "test", ActionArgument = Guid.NewGuid().ToString() }];
        }
    }

    private sealed class BlockingProvider : IInstantResultProvider, IDisposable
    {
        public string Name => "Blocking";
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim Release = new();
        public int Calls;

        public IEnumerable<InstantResultItem> GetInstantResults(string query) => GetInstantResults(query, null);

        public IEnumerable<InstantResultItem> GetInstantResults(string query, string? contextDirectory)
        {
            Interlocked.Increment(ref Calls);
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Test did not release the controlled provider.");
            return [new InstantResultItem { Title = "Late provider result", ActionArgument = Guid.NewGuid().ToString() }];
        }

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private static AppSearchResult ProvidedRow(string name) =>
        new() { Name = name, FullPath = $"__INSTANT_RESULT__:{name}", ResultKind = "InstantResult" };

    private static List<SearchResult> TwoFiles() =>
    [
        new SearchResult { Path = @"C:\Notes\alpha.txt", Name = "alpha.txt" },
        new SearchResult { Path = @"C:\Notes\beta.txt", Name = "beta.txt" },
    ];

    private static List<SearchResult> PclFiles() =>
    [
        new SearchResult { Path = @"C:\Notes\pcl-alpha.txt", Name = "pcl-alpha.txt", Drive = "C" },
        new SearchResult { Path = @"C:\Notes\pcl-beta.txt", Name = "pcl-beta.txt", Drive = "C" },
    ];

    [TestMethod]
    public void CachedPass_LandsAheadOfTheRankedFileRowsAndIsNumberedWithThem()
    {
        var cached = new SearchResultMapper.InstantPassCache { Collected = true };
        cached.Rows.Add(ProvidedRow("Calculator"));

        var result = SearchResultMapper.BuildQuickResults(
            TwoFiles(), "alpha", scope: null, contextDirectory: null, isInlineWindow: false,
            instantPass: cached);

        Assert.AreEqual("Calculator", result[0].Name, "a plugin row keeps its place at the head of the list");
        Assert.AreEqual(0, result[0].Index);
        CollectionAssert.AreEqual(
            new[] { @"C:\Notes\alpha.txt", @"C:\Notes\beta.txt" },
            result.Skip(1).Select(row => row.FullPath).ToList());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 }, result.Select(row => row.Index).ToList(), "positions are restamped for this render");
    }

    [TestMethod]
    public void OneCacheAcrossTwoPaints_SharesTheRowInstancesWithoutLosingPositions()
    {
        // The streaming case: paint 1 saw two files, paint 2 has more arrived. The cached row is one object
        // handed to both renders, which is only sound if each render still numbers it where it actually put
        // it -- the list control reconciles on row identity.
        var cached = new SearchResultMapper.InstantPassCache { Collected = true };
        cached.Rows.Add(ProvidedRow("Web result"));

        var firstPaint = SearchResultMapper.BuildQuickResults(
            TwoFiles(), "alpha", scope: null, contextDirectory: null, isInlineWindow: false, instantPass: cached);
        var secondPaint = SearchResultMapper.BuildQuickResults(
            [.. TwoFiles(), new SearchResult { Path = @"C:\Notes\gamma.txt", Name = "gamma.txt" }],
            "alpha", scope: null, contextDirectory: null, isInlineWindow: false, instantPass: cached);

        Assert.AreSame(firstPaint[0], secondPaint[0], "the cached row is reused, not rebuilt");
        Assert.AreEqual(0, secondPaint[0].Index);
        Assert.HasCount(4, secondPaint);
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, secondPaint.Select(row => row.Index).ToList());
    }
}
