using System.Runtime.InteropServices;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.SearchIndex.Fzf;

using Lertaro.Core.IndexV2.Alias;

using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.Core.SearchIndex;
namespace Lertaro.Core.IndexV2.Persistence;

// Builds a snapshot file straight from a scanner's FileRecordStore -- no RuntimeIndex intermediate.
// Soft-deleted records are folded away, survivors are re-sorted by id (stable by input order for
// hard-link ties), names dedupe into a UTF-8 arena with per-unique masks/aliases, and parent links
// resolve against the sorted id column; unresolved parents keep their true FRN in the orphan section
// so later updates can heal them, exactly like RuntimeIndex's orphan stash.
public static class SnapshotWriter
{
    // Every distinct per-drive cache path gets its own lock object, shared by every caller of Write
    // regardless of which FileRecordStore/LiveIndex instance they hold -- a network drive's periodic scan
    // checkpoint (NetworkIndex.FromStore, a brand-new LiveIndex each time) and its FileSystemWatcher's
    // incremental updates (PublishIncrementalUpdate, running against the drive's PREVIOUS still-live
    // LiveIndex/Snapshot) are two entirely separate objects with no lock in common, so nothing previously
    // stopped both from writing THIS SAME path's ".tmp" file at once. That's a real race, not a rare one:
    // the wider the file (a large network share takes longer to serialize), the longer the window both
    // can be mid-write at the same time, which is exactly what turned "occasionally" into "every time" on
    // large shares -- see the GitHub issue this fixes for the full failure signature (temp file "in use",
    // then File.Replace failing on the final path).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _pathLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static object GetPathLock(string path) => _pathLocks.GetOrAdd(path, static _ => new object());

    public static void Write(FileRecordStore store, string path)
    {
        var records = store.Records;
        using var readLease = records.AcquireReadLease();
        using var snapshot = readLease.BuildSnapshot(store.RootId);
        var uniqueNames = new string[snapshot.UniqueCount];
        for (var uid = 0; uid < uniqueNames.Length; uid++)
            uniqueNames[uid] = readLease.GetName(snapshot.UniqueNameIds[uid]);
        var aliasFingerprint = AliasProviderRegistry.ComputeProvidersFingerprint();
        WriteColumns(snapshot, uniqueNames, new SnapshotFormat.Meta
        {
            SourceKey = store.SourceKey,
            SourceRoot = PathHelpers.BuildSourceRoot(store.SourceKey),
            SourceKind = store.SourceKind,
            IdKind = store.IdKind,
            FileSystemType = store.FileSystemType,
            VolumeSerialNumber = store.VolumeSerialNumber,
            RootId = store.RootId,
            JournalId = store.JournalId,
            NextUsn = store.NextUsn,
            IsComplete = store.IsComplete,
            ExclusionRulesFingerprint = store.ExclusionRulesFingerprint,
            LastUpdated = store.LastUpdated,
        }, path, null, aliasFingerprint);
    }

    internal static void WriteCompacted(Snapshot baseSnapshot, DeltaOverlay delta, string path,
        CompactionStamp stamp, Action? beforeBackupDelete = null)
    {
        var aliasFingerprint = AliasProviderRegistry.ComputeProvidersFingerprint();
        // A compaction usually changes only a small delta. Keep the already-baked aliases for
        // unchanged names when the provider set is identical; changed/new names still go through
        // the normal generator below. A provider fingerprint mismatch deliberately disables reuse
        // so a provider upgrade (or a provider setting included in its version) fully re-bakes.
        var aliasSource = baseSnapshot.Meta.AliasEntryCount > 0
            && string.Equals(baseSnapshot.AliasProvidersFingerprint, aliasFingerprint, StringComparison.Ordinal)
            ? baseSnapshot
            : null;
        using var snapshot = NativeCompactionInterop.Build(baseSnapshot, delta, out var uniqueNames);
        WriteColumns(snapshot, uniqueNames, new SnapshotFormat.Meta
        {
            SourceKey = baseSnapshot.SourceKey,
            SourceRoot = PathHelpers.BuildSourceRoot(baseSnapshot.SourceKey),
            SourceKind = baseSnapshot.SourceKind,
            IdKind = baseSnapshot.IdKind,
            FileSystemType = baseSnapshot.FileSystemType,
            VolumeSerialNumber = baseSnapshot.VolumeSerialNumber,
            RootId = baseSnapshot.RootId,
            JournalId = stamp.JournalId ?? baseSnapshot.JournalId,
            NextUsn = stamp.NextUsn ?? baseSnapshot.NextUsn,
            IsComplete = stamp.IsComplete ?? baseSnapshot.IsComplete,
            ExclusionRulesFingerprint = stamp.ExclusionRulesFingerprint ?? baseSnapshot.ExclusionRulesFingerprint,
            LastUpdated = stamp.LastUpdated ?? baseSnapshot.LastUpdated,
        }, path, aliasSource, aliasFingerprint, beforeBackupDelete);
    }

    private static void WriteColumns(NativeSnapshotBuilder snapshot, IReadOnlyList<string> uniqueNames,
        SnapshotFormat.Meta meta, string path, Snapshot? aliasSource, string aliasFingerprint,
        Action? beforeBackupDelete = null)
    {
        var count = snapshot.Count;
        var nameIds = snapshot.NameIds;
        var flags = snapshot.Flags;
        var parentIndexes = snapshot.ParentIndexes;
        var ids = snapshot.Ids;
        var sizes = snapshot.Sizes;
        var recursiveSizes = snapshot.RecursiveSizes;
        var creation = snapshot.CreationTimes;
        var lastWrite = snapshot.LastWriteTimes;
        var lastAccess = snapshot.LastAccessTimes;

        var nameByteCount = 0;
        foreach (var name in uniqueNames)
            nameByteCount = checked(nameByteCount + SnapshotFormat.NameEncoding.GetByteCount(name));

        var nameBlob = new MemoryStream(nameByteCount);
        var nameOffsets = new List<uint>(uniqueNames.Count + 1) { 0 };
        var masks = new List<ulong>(uniqueNames.Count);
        var aliasStarts = new List<int>(uniqueNames.Count + 1);
        var aliasEntryOffsets = new List<uint>(uniqueNames.Count + 1) { 0 };
        var aliasProviderIds = new List<byte>();
        var aliasBlob = new MemoryStream();
        byte[] nameEncodingBuffer = Array.Empty<byte>();
        for (var uid = 0; uid < uniqueNames.Count; uid++)
        {
            var name = uniqueNames[uid];
            var required = SnapshotFormat.NameEncoding.GetMaxByteCount(name.Length);
            if (nameEncodingBuffer.Length < required)
                nameEncodingBuffer = new byte[required];
            var written = SnapshotFormat.NameEncoding.GetBytes(name.AsSpan(), nameEncodingBuffer.AsSpan());
            nameBlob.Write(nameEncodingBuffer, 0, written);
            nameOffsets.Add(checked((uint)nameBlob.Length));
        }

        // Alias generation is per-name-independent and dominates full-rebuild CPU on CJK-heavy
        // volumes, so it runs as a parallel pre-pass (byte-native via GetAliasesUtf8, no alias
        // strings materialized). Compaction can skip that work for names carried unchanged from the
        // base snapshot; the blob writes below stay strictly serial in uid order.
        var uniqueCount = uniqueNames.Count;
        var uniqueMasks = new ulong[uniqueCount];
        var aliasResults = new AliasGenerationUtf8.Result?[uniqueCount];
        var reusedAliasUids = new int[uniqueCount];
        Array.Fill(reusedAliasUids, -1);
        Dictionary<string, int>? sourceUids = null;
        if (aliasSource != null)
        {
            sourceUids = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var uid = 0; uid < aliasSource.UniqueCount; uid++)
            {
                if (aliasSource.HasAliases(uid))
                    sourceUids.TryAdd(aliasSource.GetUniqueName(uid), uid);
            }
        }

        Parallel.For(0, uniqueCount, uid =>
        {
            if (sourceUids != null && sourceUids.TryGetValue(uniqueNames[uid], out var sourceUid))
            {
                reusedAliasUids[uid] = sourceUid;
                uniqueMasks[uid] = aliasSource!.UniqueMasks[sourceUid];
                return;
            }

            // Union of name + alias chars: over-admits, never rejects a real match -- the
            // structural equivalent of the old engine bucketing rows under alias chars.
            var mask = FzfAlgorithm.GetCharMask(uniqueNames[uid]);
            aliasResults[uid] = AliasGenerationUtf8.Generate(uniqueNames[uid], ref mask);
            uniqueMasks[uid] = mask;
        });

        for (var uid = 0; uid < uniqueCount; uid++)
        {
            aliasStarts.Add(aliasProviderIds.Count);
            var sourceUid = reusedAliasUids[uid];
            if (sourceUid >= 0)
            {
                var (sourceStart, sourceEnd) = aliasSource!.AliasEntryRange(sourceUid);
                for (var e = sourceStart; e < sourceEnd; e++)
                {
                    aliasBlob.Write(aliasSource.AliasUtf8(e));
                    aliasEntryOffsets.Add(checked((uint)aliasBlob.Length));
                    aliasProviderIds.Add(aliasSource.AliasProviderId(e));
                }
                masks.Add(uniqueMasks[uid]);
                continue;
            }

            var result = aliasResults[uid];
            if (result != null)
            {
                var res = result.Value;
                var offset = 0;
                for (var s = 0; s < res.SegmentLengths.Length; s++)
                {
                    aliasBlob.Write(res.Bytes, offset, res.SegmentLengths[s]);
                    offset += res.SegmentLengths[s];
                    aliasEntryOffsets.Add(checked((uint)aliasBlob.Length));
                    aliasProviderIds.Add(res.ProviderIds[s]);
                }
            }
            masks.Add(uniqueMasks[uid]);
        }
        aliasStarts.Add(aliasProviderIds.Count);

        // Parent resolution against the sorted id column; unresolved links keep their FRN so a later
        // delta (or the next compaction) can heal them once the parent appears.
        var childStarts = snapshot.ChildStarts;
        var children = snapshot.Children;
        var uidStarts = snapshot.UidStarts;
        var uidRows = snapshot.UidRows;
        var orphanRows = snapshot.OrphanRows;
        var orphanFrns = snapshot.OrphanFrns;

        var asciiBits = new ulong[(uniqueNames.Count + 63) / 64];
        {
            var blob = nameBlob.GetBuffer();
            for (var uid = 0; uid < uniqueNames.Count; uid++)
            {
                var start = (int)nameOffsets[uid];
                var length = (int)(nameOffsets[uid + 1] - nameOffsets[uid]);
                if (System.Text.Ascii.IsValid(blob.AsSpan(start, length)))
                    asciiBits[uid >> 6] |= 1UL << (uid & 63);
            }
        }

        meta.RowCount = count;
        meta.UniqueCount = uniqueNames.Count;
        meta.NameBlobLength = (int)nameBlob.Length;
        meta.ChildrenLength = children.Length;
        meta.AliasEntryCount = aliasProviderIds.Count;
        meta.AliasBlobLength = (int)aliasBlob.Length;
        meta.OrphanCount = snapshot.OrphanCount;
        meta.TotalFiles = snapshot.TotalFiles;
        meta.TotalDirs = snapshot.TotalDirs;
        // Stamped fresh on every write (not carried over from the source) -- this is exactly the
        // moment every unique name's aliases just got (re)generated above.
        meta.AliasProvidersFingerprint = aliasFingerprint;

        // FileStream(..., FileMode.Create) never creates missing parent directories itself -- on a truly
        // fresh machine (no prior run has ever written here), %ProgramData%\Lertaro\indexes doesn't
        // exist yet (only Logger.cs creates its own log directory, a separate sibling path), so the very
        // first snapshot write of the session would otherwise fail with DirectoryNotFoundException.
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        // Serializes the temp-write-then-replace sequence per target path -- see GetPathLock's own
        // comment for why two entirely unrelated LiveIndex instances can otherwise both be mid-write to
        // this exact temp file at once.
        lock (GetPathLock(path))
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                using var writer = new BinaryWriter(stream, SnapshotFormat.NameEncoding, leaveOpen: true);
                SnapshotFormat.WriteHeader(writer, meta);
                SnapshotFormat.FinishHeader(stream, meta);
                var offsets = SnapshotFormat.ComputeSectionOffsets(meta, out var totalLength);

                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.NameIds, MemoryMarshal.AsBytes(nameIds));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.Flags, MemoryMarshal.AsBytes(flags));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.ParentIndexes, MemoryMarshal.AsBytes(parentIndexes));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.UniqueMasks, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(masks)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.NameOffsets, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(nameOffsets)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.NameBlob, nameBlob.GetBuffer().AsSpan(0, (int)nameBlob.Length));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.Ids, MemoryMarshal.AsBytes(ids));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.Sizes, MemoryMarshal.AsBytes(sizes));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.RecursiveSizes, MemoryMarshal.AsBytes(recursiveSizes));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.CreationTimes, MemoryMarshal.AsBytes(creation));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.LastWriteTimes, MemoryMarshal.AsBytes(lastWrite));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.LastAccessTimes, MemoryMarshal.AsBytes(lastAccess));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.ChildStarts, MemoryMarshal.AsBytes(childStarts));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.Children, MemoryMarshal.AsBytes(children));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.UidStarts, MemoryMarshal.AsBytes(uidStarts));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.UidRows, MemoryMarshal.AsBytes(uidRows));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.AliasStarts, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(aliasStarts)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.AliasEntryOffsets, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(aliasEntryOffsets)));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.AliasProviderIds, CollectionsMarshal.AsSpan(aliasProviderIds));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.AliasBlob, aliasBlob.GetBuffer().AsSpan(0, (int)aliasBlob.Length));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.OrphanRows, MemoryMarshal.AsBytes(orphanRows));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.OrphanFrns, MemoryMarshal.AsBytes(orphanFrns));
                SnapshotWriterOps.WriteSection(stream, offsets, SnapshotSection.UniqueAsciiBits, MemoryMarshal.AsBytes(asciiBits));
                stream.SetLength(totalLength);
                // Disposing the stream flushes to the OS cache, not to the platter, and the File.Replace
                // that follows is a namespace operation that can reach the disk first -- a power loss
                // then leaves the live path pointing at a zero-length or torn snapshot.
                stream.Flush(flushToDisk: true);
            }

            FileRecordStoreReplaceHelper.ReplaceWithRetry(temp, path, SnapshotWriterOps.TryDelete,
                beforeBackupDelete);
        }
    }

    internal static int FirstRowForId(UInt128[] ids, UInt128 id) => SnapshotWriterOps.FirstRowForId(ids, id);
}
