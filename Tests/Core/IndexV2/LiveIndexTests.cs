using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Tests.IndexV2;

[TestClass]
public sealed class LiveIndexTests
{
    // Regression coverage for the orphaned/stuck .bak file: direct native compaction keeps the old
    // mapping only while it builds and writes the replacement, then releases it before the backup is
    // deleted. The persisted data must still be exactly what was mutated in, and a new reader must be
    // able to open the result.
    [TestMethod]
    public void Compact_WithPendingChanges_PersistsTheMergeAndLeavesAFreshlyOpenableFile()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
        });

        fixture.Index.Mutate((_, delta) =>
            DeltaLinkOps.AddLink(delta, frn: 2, parentFrn: 1, name: "new-file.txt", FileRecordFlags.None));

        Assert.IsGreaterThan(0, fixture.Index.PendingChangeCount);

        var compacted = fixture.Index.Compact(fixture.Path, force: false);

        Assert.IsTrue(compacted);
        Assert.AreEqual(0, fixture.Index.PendingChangeCount);

        var (files, dirs) = fixture.Index.GetCounts();
        Assert.AreEqual(1, files);
        Assert.AreEqual(1, dirs);

        // A totally independent reader must be able to open the just-compacted file -- proves the swap
        // left `path` in a valid, unlocked state, not still exclusively held by the disposed old mapping.
        using var independentReader = Snapshot.Open(fixture.Path);
        Assert.AreEqual(2, independentReader.Count);
        Assert.IsFalse(File.Exists(fixture.Path + ".bak"));
    }

    [TestMethod]
    public void Compact_NoPendingChangesAndNotForced_ReturnsFalseAndLeavesFileUntouched()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
        });
        var lastWriteBefore = File.GetLastWriteTimeUtc(fixture.Path);

        var compacted = fixture.Index.Compact(fixture.Path, force: false);

        Assert.IsFalse(compacted);
        Assert.AreEqual(lastWriteBefore, File.GetLastWriteTimeUtc(fixture.Path));
    }

    [TestMethod]
    public void Compact_ForcedWithNoPendingChanges_StillPersistsAndReturnsTrue()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
        });

        var compacted = fixture.Index.Compact(fixture.Path, force: true);

        Assert.IsTrue(compacted);
        var (files, dirs) = fixture.Index.GetCounts();
        Assert.AreEqual(0, files);
        Assert.AreEqual(1, dirs);
    }

    [TestMethod]
    public void Compact_ReplacementFails_PreservesPendingChangesAndCanRetry()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        fixture.Index.Mutate((_, delta) =>
            DeltaLinkOps.AddLink(delta, frn: 2, parentFrn: 1, name: "pending.txt", FileRecordFlags.None));
        var pending = fixture.Index.PendingChangeCount;
        var revision = fixture.Index.Revision;

        using (var blocked = new FileStream(fixture.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Assert.Throws<IOException>(() => fixture.Index.Compact(fixture.Path));
            Assert.AreEqual(pending, fixture.Index.PendingChangeCount);
            Assert.AreEqual(revision, fixture.Index.Revision);
            Assert.AreEqual((1, 1), fixture.Index.GetCounts());
        }

        Assert.IsTrue(fixture.Index.Compact(fixture.Path));
        Assert.AreEqual(0, fixture.Index.PendingChangeCount);
        using var reopened = Snapshot.Open(fixture.Path);
        Assert.AreEqual(2, reopened.Count);
    }

    [TestMethod]
    public void Compact_TargetBecomesDirectory_KeepsTheOriginalMappingUsable()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
        });

        fixture.Index.Mutate((_, delta) =>
            DeltaLinkOps.AddLink(delta, frn: 2, parentFrn: 1, name: "pending.txt", FileRecordFlags.None));
        File.Delete(fixture.Path);
        Directory.CreateDirectory(fixture.Path);

        Assert.Throws<UnauthorizedAccessException>(() => fixture.Index.Compact(fixture.Path, force: true));
        Assert.AreEqual((1, 1), fixture.Index.GetCounts());
        Assert.IsGreaterThan(0, fixture.Index.PendingChangeCount);
        Directory.Delete(fixture.Path);
        Assert.IsTrue(fixture.Index.Compact(fixture.Path));
        Assert.AreEqual((1, 1), fixture.Index.GetCounts());
        using var reopened = Snapshot.Open(fixture.Path);
        Assert.AreEqual(2, reopened.Count);
    }

    [TestMethod]
    public void Compact_TemporaryWriteFails_PreservesSnapshotAndOverlay()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        fixture.Index.Mutate((_, delta) =>
            DeltaLinkOps.AddLink(delta, frn: 2, parentFrn: 1, name: "pending.txt", FileRecordFlags.None));
        Directory.CreateDirectory(fixture.Path + ".compact.tmp");

        Assert.Throws<UnauthorizedAccessException>(() => fixture.Index.Compact(fixture.Path));

        Assert.AreEqual((1, 1), fixture.Index.GetCounts());
        Assert.IsGreaterThan(0, fixture.Index.PendingChangeCount);
        using var disk = Snapshot.Open(fixture.Path);
        Assert.AreEqual(1, disk.Count);
        Directory.Delete(fixture.Path + ".compact.tmp");
        Assert.IsTrue(fixture.Index.Compact(fixture.Path));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void Compact_OpenFailsBeforeOrAfterReplacement_RetainsOverlayAndRetries(int failedOpen)
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        fixture.Index.Mutate((_, delta) =>
            DeltaLinkOps.AddLink(delta, frn: 2, parentFrn: 1, name: "pending.txt", FileRecordFlags.None));
        var revision = fixture.Index.Revision;
        var expected = new IOException("Cannot map the new snapshot.");
        var opens = 0;

        var actual = Assert.Throws<IOException>(() => fixture.Index.Compact(fixture.Path, default, false, path =>
        {
            if (++opens == failedOpen) throw expected;
            return Snapshot.Open(path);
        }));

        Assert.AreSame(expected, actual);
        Assert.AreEqual(revision, fixture.Index.Revision);
        Assert.AreEqual((1, 1), fixture.Index.GetCounts());
        Assert.IsGreaterThan(0, fixture.Index.PendingChangeCount);
        using (var disk = Snapshot.Open(fixture.Path))
            Assert.AreEqual(failedOpen == 1 ? 1 : 2, disk.Count);
        if (failedOpen == 2)
            Assert.IsTrue(File.Exists(fixture.Path + ".bak"));
        Assert.IsTrue(fixture.Index.Compact(fixture.Path));
        Assert.AreEqual(0, fixture.Index.PendingChangeCount);
        Assert.IsFalse(File.Exists(fixture.Path + ".bak"));
        using var reopened = Snapshot.Open(fixture.Path);
        Assert.AreEqual(2, reopened.Count);
    }

    [TestMethod]
    public void Compact_WhilePreparing_ReadersContinueAndQueuedMutationSurvives()
    {
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        fixture.Index.Mutate((_, delta) =>
            DeltaLinkOps.AddLink(delta, frn: 2, parentFrn: 1, name: "before.txt", FileRecordFlags.None));
        Exception? error = null;
        using var started = new ManualResetEventSlim();
        var compactor = new Thread(() =>
        {
            started.Set();
            try { fixture.Index.Compact(fixture.Path); }
            catch (Exception ex) { error = ex; }
        });
        Task? mutation = null;
        try
        {
            // Pause the real writer before it writes the candidate, not via a fake compaction API.
            lock (SnapshotWriter.GetPathLock(fixture.Path + ".compact"))
            {
                compactor.Start();
                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)));
                Assert.IsTrue(SpinWait.SpinUntil(() =>
                    (compactor.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                    TimeSpan.FromSeconds(10)));
                var read = Task.Run(() =>
                {
                    var results = new List<SearchResult>();
                    IndexV2Searcher.SearchStreaming(fixture.Index, "before.txt", 20, results.Add,
                        CancellationToken.None);
                    return results;
                });
                Assert.IsTrue(read.Wait(TimeSpan.FromSeconds(10)), "Search was blocked by preparation.");
                Assert.HasCount(1, read.Result);
                mutation = Task.Run(() => fixture.Index.Mutate((_, delta) =>
                    DeltaLinkOps.AddLink(delta, frn: 3, parentFrn: 1, name: "after.txt", FileRecordFlags.None)));
                Assert.IsFalse(mutation.Wait(TimeSpan.FromMilliseconds(100)));
            }
        }
        finally
        {
            Assert.IsTrue(compactor.Join(TimeSpan.FromSeconds(30)));
            if (mutation != null)
                Assert.IsTrue(mutation.Wait(TimeSpan.FromSeconds(30)));
        }
        Assert.IsNull(error);
        Assert.AreEqual((2, 1), fixture.Index.GetCounts());
        Assert.IsGreaterThan(0, fixture.Index.PendingChangeCount);
        using (var disk = Snapshot.Open(fixture.Path))
            Assert.AreEqual(2, disk.Count);
        Assert.IsTrue(fixture.Index.Compact(fixture.Path));
        using var final = Snapshot.Open(fixture.Path);
        Assert.AreEqual(3, final.Count);
    }

}
