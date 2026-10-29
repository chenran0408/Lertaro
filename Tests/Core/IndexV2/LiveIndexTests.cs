using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;

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
    public void Compact_DoubleFailure_EntersObjectDisposedStateInsteadOfLeavingDanglingSnapshot()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
        });

        // Force both steps of the compact double-failure path: SnapshotWriter.Write cannot replace a
        // directory, and Snapshot.Open also cannot open a directory. The old mapping has already been
        // disposed by then, so Compact must transition into a clear invalid state rather than leaving a
        // disposed Snapshot behind for later queries to dereference.
        File.Delete(fixture.Path);
        Directory.CreateDirectory(fixture.Path);

        Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Index.Compact(fixture.Path, force: true));
        Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Index.GetCounts());
    }

}
