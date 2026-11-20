using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class PathTermFallbackTests
{
    // A layout where many files share one name and are told apart only by the folder above them --
    // the shape the ancestor pass exists for. Names here are generic on purpose (repo rule 15).
    private static LiveIndexFixture BuildSeriesDrive() => LiveIndexFixture.Build("T", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "library", FileRecordFlags.Directory),
        new FileRecord(3, 2, "drama", FileRecordFlags.Directory),
        new FileRecord(4, 3, "Rose Season", FileRecordFlags.Directory),
        new FileRecord(5, 4, "Episode01.mp4", FileRecordFlags.None),
        new FileRecord(6, 3, "Tulip Season", FileRecordFlags.Directory),
        new FileRecord(7, 6, "Episode01.mp4", FileRecordFlags.None),
        new FileRecord(8, 3, "Rose Notes.txt", FileRecordFlags.None),
    });

    private static List<SearchResult> Search(LiveIndexFixture fixture, string query, int limit = 10)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, limit, results.Add, CancellationToken.None);
        return results;
    }

    private static LiveIndexFixture BuildAndFirstDrive() => LiveIndexFixture.Build("T", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "summary", FileRecordFlags.Directory),
        new FileRecord(3, 2, "2024.txt", FileRecordFlags.None),
    });

    [TestMethod]
    public void BoundedAncestorMatchesAgreeWithTheExhaustivePrefixIncludingTies()
    {
        var records = new List<FileRecord> { LiveIndexFixture.Root(), new(2, 1, "orchid", FileRecordFlags.Directory) };
        for (var i = 0; i < 1300; i++)
            records.Add(new FileRecord((ulong)(i + 3), 2, $"report-{i:D4}.txt", FileRecordFlags.None));
        using var fixture = LiveIndexFixture.Build("T", records);
        var all = Search(fixture, "orchid report", int.MaxValue);
        Assert.HasCount(1300, all);
        CollectionAssert.AreEqual(all.Take(12).Select(row => row.Path).ToArray(),
            Search(fixture, "orchid report", 12).Select(row => row.Path).ToArray());
        Assert.IsEmpty(Search(fixture, "unrelated report", 12));
    }

    [TestMethod]
    public void CachedTermDoesNotCrossAliasSettingsOrSnapshots()
    {
        using var first = BuildSeriesDrive();
        using var second = BuildSeriesDrive();
        var pattern = FzfPattern.Parse("rose");
        var original = SearchContext.DisabledAliasIds;
        try
        {
            SearchContext.DisabledAliasIds = null;
            first.Index.Read((snapshot, overlay) =>
            {
                Assert.IsFalse(PathTermMatchCache.TryGet(snapshot, pattern, out var hits, out var save));
                var matches = new List<UniqueMatch>();
                SearchMatcher.MatchUniques(snapshot, pattern, matches);
                save!(matches);
                Assert.IsTrue(PathTermMatchCache.TryGet(snapshot, pattern, out hits, out _));
                Assert.AreEqual(matches.Count, hits.Length);
                SearchContext.DisabledAliasIds = [12];
                Assert.IsFalse(PathTermMatchCache.TryGet(snapshot, pattern, out hits, out _));
                return true;
            });
            SearchContext.DisabledAliasIds = null;
            Assert.IsFalse(second.Index.Read((snapshot, overlay) =>
                PathTermMatchCache.TryGet(snapshot, pattern, out _, out _)));
        }
        finally
        {
            SearchContext.DisabledAliasIds = original;
        }
    }

    [TestMethod]
    public void RefiningTheFolderTermKeepsOnlyTheMatchingPath()
    {
        using var fixture = BuildSeriesDrive();
        var first = Search(fixture, "episode01 rose");
        var refined = Search(fixture, "episode01 tulip");
        var restored = Search(fixture, "episode01 rose");

        CollectionAssert.AreEqual(new[] { @"T:\library\drama\Rose Season\Episode01.mp4" }, first.Select(row => row.Path).ToArray());
        CollectionAssert.AreEqual(new[] { @"T:\library\drama\Tulip Season\Episode01.mp4" }, refined.Select(row => row.Path).ToArray());
        CollectionAssert.AreEqual(first.Select(row => row.Path).ToArray(), restored.Select(row => row.Path).ToArray());
    }

    [TestMethod]
    public void SearchStreaming_AndFirstBranchStillUsesAncestorFallback()
    {
        using var fixture = BuildAndFirstDrive();

        // The file name supplies only "2024"; "summary" is supplied by its parent folder. This row
        // is reachable only if the mixed-precedence path splits the DNF branches and runs the existing
        // ancestor fallback for the [summary AND 2024] branch.
        var results = Search(fixture, "report | summary 2024");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\summary\2024.txt", results[0].Path);
    }

    // The ancestor verdict is now recorded for every folder on the chain, not just the one asked about,
    // so a later row can be answered from a partial walk somebody else did. The cases below are the ones
    // where sharing an answer could hand back the wrong one.
    [TestMethod]
    public void SearchStreaming_SiblingFoldersUnderOneAncestor_DoNotInheritEachOthersVerdict()
    {
        using var fixture = BuildSeriesDrive();

        // Both series folders sit under the same "drama", so walking one records an answer for "drama"
        // and everything above it. The other must still be judged on its own name: sharing the ancestor
        // must not make "rose" match a file under Tulip Season.
        var rose = Search(fixture, "episode01 rose");

        Assert.HasCount(1, rose);
        Assert.AreEqual(@"T:\library\drama\Rose Season\Episode01.mp4", rose[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_ATermSatisfiedHighOnTheChain_ReachesEveryFolderBelowIt()
    {
        using var fixture = BuildSeriesDrive();

        // "library" is satisfied at the top of the chain, so it holds for both series folders. Walking
        // the first one records that; the second must get the same answer rather than a truncated one.
        var results = Search(fixture, "episode01 library");

        Assert.HasCount(2, results);
        CollectionAssert.AreEquivalent(
            new[] { @"T:\library\drama\Rose Season\Episode01.mp4", @"T:\library\drama\Tulip Season\Episode01.mp4" },
            results.ConvertAll(r => r.Path));
    }

    [TestMethod]
    public void SearchStreaming_AfterAnAncestorFolderIsRenamed_MatchesTheNewName()
    {
        using var fixture = BuildSeriesDrive();
        fixture.Index.Mutate((_, delta) =>
            delta.Upsert(6, 3, "Iris Season", FileRecordFlags.None | FileRecordFlags.Directory, 0, 0, 0, 0));

        // A renamed ancestor lives only in delta state, so the walk falls back to the built path string.
        // That answer describes one starting folder and must not be recorded against the folders above
        // it, which are unchanged and shared with the other series.
        var byNewName = Search(fixture, "episode01 iris");
        var byOldName = Search(fixture, "episode01 tulip");
        var sibling = Search(fixture, "episode01 rose");

        Assert.HasCount(1, byNewName);
        Assert.AreEqual(@"T:\library\drama\Iris Season\Episode01.mp4", byNewName[0].Path);
        Assert.IsEmpty(byOldName);
        Assert.HasCount(1, sibling);
        Assert.AreEqual(@"T:\library\drama\Rose Season\Episode01.mp4", sibling[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_TermMatchingOnlyAnAncestorFolder_StillMatches()
    {
        using var fixture = BuildSeriesDrive();

        // "episode01" hits the file name, "tulip" only the series folder above it.
        var results = Search(fixture, "episode01 tulip");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama\Tulip Season\Episode01.mp4", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_AncestorTermsAreOrderFree()
    {
        using var fixture = BuildSeriesDrive();

        // Same two terms typed the other way round: unlike path mode, no positional meaning.
        var results = Search(fixture, "tulip episode01");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama\Tulip Season\Episode01.mp4", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_TermSatisfiedByAGrandparent_StillMatches()
    {
        using var fixture = BuildSeriesDrive();

        // "library" is two levels above the file, so this only works if the whole chain is walked.
        var results = Search(fixture, "episode01 library");

        Assert.HasCount(2, results);
        CollectionAssert.AreEquivalent(
            new[] { @"T:\library\drama\Rose Season\Episode01.mp4", @"T:\library\drama\Tulip Season\Episode01.mp4" },
            results.Select(r => r.Path).ToList());
    }

    [TestMethod]
    public void SearchStreaming_TopUpAddsNothingWhenNoAncestorSatisfiesTheRest()
    {
        using var fixture = BuildSeriesDrive();

        // "Rose Notes.txt" answers both terms by name. The ancestor pass still runs alongside it, but
        // nothing above the "Rose Season" folder answers "notes", so it contributes nothing and the
        // episodes underneath stay out.
        var results = Search(fixture, "rose notes");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama\Rose Notes.txt", results[0].Path);
    }

    // The shape from the field report: the same two terms are answered both by one file's name and by
    // a folder that several other files sit under.
    private static LiveIndexFixture BuildOverlapDrive() => LiveIndexFixture.Build("Z", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Tulip Report 2024.pdf", FileRecordFlags.None),
        new FileRecord(3, 1, "Tulip Archive", FileRecordFlags.Directory),
        new FileRecord(4, 3, "Report A.pdf", FileRecordFlags.None),
        new FileRecord(5, 3, "Report B.pdf", FileRecordFlags.None),
    });

    [TestMethod]
    public void SearchStreaming_AnIncidentalNameHitDoesNotSuppressTheAncestorPass()
    {
        using var fixture = BuildOverlapDrive();

        // Gating the ancestor pass on an empty result set meant "Tulip Report 2024.pdf" alone -- one
        // file happening to carry both words -- hid every file under the folder that answers "tulip".
        var results = Search(fixture, "tulip report");

        CollectionAssert.AreEquivalent(
            new[]
            {
                @"Z:\Tulip Report 2024.pdf",
                @"Z:\Tulip Archive\Report A.pdf",
                @"Z:\Tulip Archive\Report B.pdf",
            },
            results.Select(r => r.Path).ToList());
    }

    [TestMethod]
    public void SearchStreaming_NameHitsComeBeforeAncestorOnes()
    {
        using var fixture = BuildOverlapDrive();

        var results = Search(fixture, "tulip report");

        // The pass appends rather than merges, which is what keeps a genuine name match from being
        // pushed down by a weaker path-derived one.
        Assert.AreEqual(@"Z:\Tulip Report 2024.pdf", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_TopUpRespectsTheRemainingLimit()
    {
        using var fixture = BuildOverlapDrive();

        // The ancestor pass gets what the name hits left over, not the caller's whole limit again.
        var results = Search(fixture, "tulip report", limit: 2);

        Assert.HasCount(2, results);
        Assert.AreEqual(@"Z:\Tulip Report 2024.pdf", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_AFullPageIsNotToppedUp()
    {
        using var fixture = BuildOverlapDrive();

        // Nothing is left to fill, so the pass never runs and costs nothing.
        var results = Search(fixture, "tulip report", limit: 1);

        Assert.HasCount(1, results);
        Assert.AreEqual(@"Z:\Tulip Report 2024.pdf", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_FolderOnlyTerms_MatchTheFolderWithoutDumpingItsContents()
    {
        using var fixture = BuildSeriesDrive();

        var results = Search(fixture, "library drama");

        // Directories are indexed rows with names of their own, so "drama" (own name) plus "library"
        // (its parent) legitimately identifies the folder itself.
        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama", results[0].Path);
        Assert.IsTrue(results[0].IsDir);
        // The point of requiring one term to hit a name: the episodes underneath match neither term,
        // so describing only their folders never dumps the whole subtree.
        Assert.IsFalse(results.Any(r => r.Name.Contains("Episode", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SearchStreaming_SingleTermQuery_IsUnaffected()
    {
        using var fixture = BuildSeriesDrive();

        // One term has nowhere to split, so a folder-only word still finds only the folder itself.
        var results = Search(fixture, "tulip");

        Assert.HasCount(1, results);
        Assert.IsTrue(results[0].IsDir);
    }

    [TestMethod]
    public void SearchStreaming_UnmatchableTerm_StillReturnsNothing()
    {
        using var fixture = BuildSeriesDrive();

        var results = Search(fixture, "episode01 nosuchfolder");

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void SearchStreaming_DirectoryFilterStillApplies()
    {
        using var fixture = BuildSeriesDrive();

        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, "episode01 library", 10, results.Add,
            CancellationToken.None, directoryFilter: @"T:\library\drama\Rose Season");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\library\drama\Rose Season\Episode01.mp4", results[0].Path);
    }
}
