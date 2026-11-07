namespace Lertaro.Core.Tests;

[TestClass]
public sealed class SearchResultRankComparerTests
{
    [TestMethod]
    public void Compare_HigherHistoryPriority_SortsBeforeUnranked()
    {
        var history = new Dictionary<string, int> { [@"c:\a.txt"] = 0 };
        var comparer = new SearchResultRankComparer(history);
        var ranked = new SearchResult { Path = @"c:\a.txt" };
        var unranked = new SearchResult { Path = @"c:\b.txt" };

        Assert.IsLessThan(0, comparer.Compare(ranked, unranked));
    }

    [TestMethod]
    public void Compare_LowerHistoryPriorityNumber_SortsFirst()
    {
        var history = new Dictionary<string, int> { [@"c:\a.txt"] = 0, [@"c:\b.txt"] = 5 };
        var comparer = new SearchResultRankComparer(history);
        var first = new SearchResult { Path = @"c:\a.txt" };
        var second = new SearchResult { Path = @"c:\b.txt" };

        Assert.IsLessThan(0, comparer.Compare(first, second));
    }

    [TestMethod]
    public void Compare_TrailingBackslash_IsNormalizedForHistoryLookup()
    {
        // A directory path with a trailing separator must still match its history entry recorded
        // without one (NormalizeForLookup strips it, but only for paths longer than "C:\").
        var history = new Dictionary<string, int> { [@"c:\folder"] = 0 };
        var comparer = new SearchResultRankComparer(history);
        var withSlash = new SearchResult { Path = @"c:\folder\" };
        var unranked = new SearchResult { Path = @"c:\other" };

        Assert.IsLessThan(0, comparer.Compare(withSlash, unranked));
    }

    [TestMethod]
    public void Compare_NoHistoryMatch_FallsBackToShorterPathFirst()
    {
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>());
        var shorter = new SearchResult { Path = @"c:\a.txt" };
        var longer = new SearchResult { Path = @"c:\a\very\long\path.txt" };

        Assert.IsLessThan(0, comparer.Compare(shorter, longer));
    }

    [TestMethod]
    public void Compare_SamePathLength_FallsBackToDriveThenPathOrdinalIgnoreCase()
    {
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>());
        var onC = new SearchResult { Path = @"c:\a.txt", Drive = "C" };
        var onD = new SearchResult { Path = @"d:\a.txt", Drive = "D" };

        Assert.IsLessThan(0, comparer.Compare(onC, onD));
    }

    [TestMethod]
    public void Compare_NullHandling_NullSortsAfterNonNull()
    {
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>());
        var result = new SearchResult { Path = @"c:\a.txt" };

        Assert.IsLessThan(0, comparer.Compare(result, null));
        Assert.IsGreaterThan(0, comparer.Compare(null, result));
        Assert.AreEqual(0, comparer.Compare(null, null));
    }

    [TestMethod]
    public void Compare_SameReference_ReturnsZero()
    {
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>());
        var result = new SearchResult { Path = @"c:\a.txt" };

        Assert.AreEqual(0, comparer.Compare(result, result));
    }

    // ═══ A2 usage tie-breaker ═══

    private static SearchResult KeyedResult(string path, ulong sortKey = 100) => new()
    {
        Name = System.IO.Path.GetFileName(path),
        Path = path,
        Drive = path.Length > 3 ? path[..1] : "C",
        RankSortKey = sortKey,
    };

    [TestMethod]
    public void Usage_AllZero_ProducesBitIdenticalOrderToTheLegacyComparer()
    {
        // The A2 regression gate: with no usage data anywhere, ordering must be exactly what the
        // pre-A2 comparer produced -- an upgrade can never look like a ranking bug.
        var paths = new[]
        {
            @"c:\short\a.txt", @"c:\a-much-longer-path\deeper\nested\b.txt", @"d:\root\c.log",
            @"c:\short\same-drive\d.md", @"e:\e.bin", @"c:\a-much-longer-path\x\e.txt",
        };
        var results = paths.Select((p, i) => KeyedResult(p, (ulong)(100 + i % 3))).ToList();

        var legacy = new SearchResultRankComparer(new Dictionary<string, int>());
        var withZeroUsage = new SearchResultRankComparer(
            new Dictionary<string, int>(), new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        var legacyOrder = results.OrderBy(r => r, legacy).Select(r => r.Path).ToList();
        var zeroUsageOrder = results.OrderBy(r => r, withZeroUsage).Select(r => r.Path).ToList();

        CollectionAssert.AreEqual(legacyOrder, zeroUsageOrder);
    }

    [TestMethod]
    public void Usage_SameSortKey_HigherUsageSortsFirst()
    {
        var low = KeyedResult(@"c:\tools\less-used.txt");
        var high = KeyedResult(@"c:\tools\often-used.txt");
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [low.Path] = 1,
            [high.Path] = 40,
        };
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>(), usage);

        Assert.IsLessThan(0, comparer.Compare(high, low));
        Assert.IsGreaterThan(0, comparer.Compare(low, high));
    }

    [TestMethod]
    public void Usage_SortKeyStillDominates()
    {
        // The tie-breaker only breaks ties: a better match never loses to a heavily-used one.
        var goodMatch = KeyedResult(@"c:\exact\report.docx", sortKey: 10);
        var usedButWorse = KeyedResult(@"c:\other\notes.txt", sortKey: 999);
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [usedButWorse.Path] = 100,
        };
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>(), usage);

        Assert.IsLessThan(0, comparer.Compare(goodMatch, usedButWorse));
    }

    [TestMethod]
    public void Usage_UnknownPaths_CountAsZero()
    {
        var known = KeyedResult(@"c:\known\used.txt");
        var unknown = KeyedResult(@"c:\unknown\never.txt");
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [known.Path] = 3 };
        var comparer = new SearchResultRankComparer(new Dictionary<string, int>(), usage);

        Assert.IsLessThan(0, comparer.Compare(known, unknown));
    }
}
