using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2;

// Fields the snapshot itself doesn't own live authority over -- LOCAL drives track JournalId/NextUsn
// externally (UsnIndexer.DriveRuntimeMetadata, updated on every USN batch, only synced to the snapshot
// at compaction time); NETWORK/WSL/folder drives similarly track IsComplete/ExclusionRulesFingerprint/
// LastUpdated externally (NetworkIndex's own mutable properties, set by the caller after a scan
// finishes). All default to "keep the snapshot's current value" when omitted.
public readonly record struct CompactionStamp(
    ulong? JournalId = null,
    long? NextUsn = null,
    bool? IsComplete = null,
    string? ExclusionRulesFingerprint = null,
    DateTime? LastUpdated = null);

// Folds a Snapshot + its DeltaOverlay into a fresh snapshot file: every live row (unmodified base,
// metadata-refreshed base, renamed/moved override, or freshly added) becomes one FileRecord, fed
// through the same SnapshotWriter a full rebuild uses -- no separate merge-format code path to keep
// in sync. Renamed-away base rows are dropped; their live identity (if any arrived) is already
// present as a delta row via ParentFrn-based attribution, matching the DeltaOverlay invariant that at
// most one live directory row exists per FRN.
public static class Compaction
{
    public static void Compact(Snapshot snapshot, DeltaOverlay delta, string path, CompactionStamp stamp = default)
        => Compact(snapshot, delta, path, stamp, beforeBackupDelete: null);

    // LiveIndex uses this hook to release the old memory mapping after File.Replace has completed but
    // before the replacement helper removes its .bak file. That keeps direct native compaction from
    // materializing a FileRecordStore while preserving the Windows mapping/rename ordering contract.
    internal static void Compact(Snapshot snapshot, DeltaOverlay delta, string path, CompactionStamp stamp,
        Action? beforeBackupDelete)
    {
        SnapshotWriter.WriteCompacted(snapshot, delta, path, stamp, beforeBackupDelete);
    }

    // The merge half of compaction, without the file write -- also backs NetworkIndex.ToStore(), which
    // needs a FileRecordStore (not a file on disk) as the next scan's TreeDiffBaseline input.
    public static FileRecordStore BuildMergedStore(Snapshot snapshot, DeltaOverlay delta, CompactionStamp stamp = default)
    {
        var store = new FileRecordStore
        {
            SourceKey = snapshot.SourceKey,
            SourceKind = snapshot.SourceKind,
            IdKind = snapshot.IdKind,
            FileSystemType = snapshot.FileSystemType,
            VolumeSerialNumber = snapshot.VolumeSerialNumber,
            RootId = snapshot.RootId,
            JournalId = stamp.JournalId ?? snapshot.JournalId,
            NextUsn = stamp.NextUsn ?? snapshot.NextUsn,
            IsComplete = stamp.IsComplete ?? snapshot.IsComplete,
            ExclusionRulesFingerprint = stamp.ExclusionRulesFingerprint ?? snapshot.ExclusionRulesFingerprint,
            LastUpdated = stamp.LastUpdated ?? snapshot.LastUpdated,
        };
        var storeTransferred = false;
        try
        {
            store.Records.Capacity = snapshot.Count + delta.VisibleAddedCount;
            // Decode each immutable base name once. Snapshot.GetName(row) would allocate the same
            // UTF-8 name again for every hard-link row during a full compaction.
            var baseNames = new string[snapshot.UniqueCount];
            for (var uid = 0; uid < baseNames.Length; uid++)
                baseNames[uid] = snapshot.GetUniqueName(uid);
            var hasBaseChanges = !delta.HasNoBaseChanges || delta.MetadataOverrides.Count != 0;

            for (var row = 0; row < snapshot.Count; row++)
            {
                if (hasBaseChanges && (delta.DeletedBase.Contains(row) || delta.RenamedAway.ContainsKey(row)))
                    continue;

                if (hasBaseChanges && delta.BaseOverrides.TryGetValue(row, out var o))
                {
                    store.Records.Add(new FileRecord(snapshot.Ids[row], o.ParentFrn, o.Name, (FileRecordFlags)o.Flags,
                        o.Size, o.Creation, o.LastWrite, o.LastAccess));
                }
                else if (!hasBaseChanges)
                {
                    var name = baseNames[(int)snapshot.NameIds[row]];
                    store.Records.Add(new FileRecord(snapshot.Ids[row], snapshot.GetParentId(row), name,
                        (FileRecordFlags)snapshot.Flags[row], snapshot.Sizes[row], snapshot.CreationTimes[row],
                        snapshot.LastWriteTimes[row], snapshot.LastAccessTimes[row]));
                }
                else
                {
                    var (size, creation, lastWrite, lastAccess) = delta.MetadataOf(row);
                    var name = baseNames[(int)snapshot.NameIds[row]];
                    store.Records.Add(new FileRecord(snapshot.Ids[row], snapshot.GetParentId(row), name,
                        (FileRecordFlags)snapshot.Flags[row], size, creation, lastWrite, lastAccess));
                }
            }

            foreach (var record in delta.Added)
            {
                if (record.Removed)
                    continue;
                store.Records.Add(new FileRecord(record.Id, record.ParentFrn, record.Name, (FileRecordFlags)record.Flags,
                    record.Size, record.Creation, record.LastWrite, record.LastAccess));
            }

            storeTransferred = true;
            return store;
        }
        finally
        {
            if (!storeTransferred)
                store.Dispose();
        }
    }
}
