using Lertaro.Core.IndexV2.Delta;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.IndexV2;

// Owns one drive's Snapshot + DeltaOverlay pair and the concurrency model the prototype never needed
// (a single-threaded benchmark has no readers to race). Snapshot itself is immutable once mapped, so
// searches never need to block each other -- only DeltaOverlay's mutable dictionaries/lists need
// protection. The mutation gate freezes writers during compaction without blocking searches. Only
// publication takes the reader/writer lock exclusively; the old mapping stays alive until readers drain.
public sealed class LiveIndex : IDisposable
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly object _mutationGate = new();
    private Snapshot? _snapshot;
    private DeltaOverlay? _delta;
    private string? _retainedBackupPath;
    private long _revision;

    public LiveIndex(Snapshot snapshot)
    {
        _snapshot = snapshot;
        _delta = new DeltaOverlay(snapshot);
    }

    public string SourceKey => _snapshot?.SourceKey ?? throw new ObjectDisposedException(nameof(LiveIndex));

    private void EnsureUsable()
    {
        if (_snapshot == null)
            throw new ObjectDisposedException(nameof(LiveIndex));
    }
    public long Revision => Interlocked.Read(ref _revision);

    // The scan-completeness marker recorded when this snapshot was written. DeltaOverlay never changes
    // it, so this reads straight off the base snapshot rather than merging through ToStore().
    public bool IsComplete => Read((snapshot, _) => snapshot.IsComplete);

    // Runs `read` under the read lock with a consistent (Snapshot, DeltaOverlay) pair -- multiple
    // searches run concurrently, but never overlap a mutation or a compaction swap.
    public T Read<T>(Func<Snapshot, DeltaOverlay, T> read)
    {
        _lock.EnterReadLock();
        try
        {
            EnsureUsable();
            return read(_snapshot!, _delta!);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    // Runs `mutate` under the write lock -- USN/watcher batches call in here so their whole batch is
    // atomic with respect to concurrent searches (a search never sees half an update batch applied).
    public void Mutate(Action<Snapshot, DeltaOverlay> mutate)
    {
        lock (_mutationGate)
        {
            _lock.EnterWriteLock();
            try
            {
                EnsureUsable();
                var delta = _delta!;
                delta.BeginMutationBatch();
                try
                {
                    mutate(_snapshot!, delta);
                }
                finally
                {
                    delta.EndMutationBatch();
                }
                Interlocked.Increment(ref _revision);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }

    public int PendingChangeCount => Read((_, delta) => delta.PendingChangeCount);

    // Current live state (base+delta merged) as a FileRecordStore -- e.g. for a network/WSL/folder
    // drive's next scan pass to use as its TreeDiffBaseline input (NetworkIndex.ToStore()).
    public FileRecordStore ToStore() => Read((snapshot, delta) => Compaction.BuildMergedStore(snapshot, delta));

    // Live totals = the base snapshot's frozen counts plus every visibility change the overlay has
    // made since -- O(1), safe to poll frequently (status bar, etc.) without rescanning the drive.
    public (int Files, int Dirs) GetCounts() => Read((snapshot, delta) =>
        (Math.Max(0, snapshot.TotalFiles + delta.FileCountDelta), Math.Max(0, snapshot.TotalDirs + delta.DirCountDelta)));

    // Folds the current Snapshot+DeltaOverlay into a fresh snapshot file at `path` and swaps it in.
    // No-op (returns false) unless `force` or there is something pending to fold -- a periodic
    // idle-triggered compactor should pass force:false to skip pointless rewrites; a caller that must
    // durably persist a new watermark regardless of file-level churn (matching the old engine's
    // SaveDrivesToCache, which always wrote) should pass force:true. `stamp` lets a caller override
    // fields the snapshot itself isn't the live authority for -- JournalId/NextUsn for local drives,
    // IsComplete/ExclusionRulesFingerprint/LastUpdated for network/WSL/folder drives; omitted fields
    // keep the snapshot's current value.
    public bool Compact(string path, CompactionStamp stamp = default, bool force = false)
        => Compact(path, stamp, force, Snapshot.Open);

    internal bool Compact(string path, CompactionStamp stamp, bool force, Func<string, Snapshot> openSnapshot)
    {
        // ponytail: incoming mutations wait for this save; searches do not. A versioned overlay can
        // remove the remaining writer pause if measurements show that ingestion needs it.
        lock (_mutationGate)
        lock (SnapshotWriter.GetPathLock(path))
        {
            EnsureUsable();
            if (!force && _delta!.PendingChangeCount == 0)
                return false;

            var oldSnapshot = _snapshot!;
            var oldDelta = _delta!;
            var preparedPath = path + ".compact";
            // After a post-replacement open failure, the old mapping still owns .bak. Do not ask
            // ReplaceFile to overwrite that mapped backup on retry (Windows refuses it).
            var backupPath = path + (_retainedBackupPath == null ? ".bak" : ".bak.next");
            try
            {
                Compaction.Compact(oldSnapshot, oldDelta, preparedPath, stamp);
                // Validate before replacing the live file. Windows cannot ReplaceFile from a mapped
                // source, so release this validation mapping before publishing.
                using (openSnapshot(preparedPath)) { }
                _lock.EnterWriteLock();
                try
                {
                    SnapshotWriter.Publish(preparedPath, path, () =>
                    {
                        // If opening fails, keep the old mapping/overlay and the backup for retry.
                        Snapshot nextSnapshot;
                        try { nextSnapshot = openSnapshot(path); }
                        catch
                        {
                            _retainedBackupPath ??= backupPath;
                            throw;
                        }
                        var nextDelta = new DeltaOverlay(nextSnapshot);
                        _snapshot = nextSnapshot;
                        _delta = nextDelta;
                        Interlocked.Increment(ref _revision);
                        oldSnapshot.Dispose();
                        if (_retainedBackupPath != null)
                        {
                            SnapshotWriterOps.TryDelete(_retainedBackupPath);
                            _retainedBackupPath = null;
                        }
                    }, backupPath);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
                return true;
            }
            finally
            {
                SnapshotWriterOps.TryDelete(preparedPath);
                SnapshotWriterOps.TryDelete(preparedPath + ".tmp");
            }
        }
    }

    // Swaps in a snapshot built OUTSIDE this LiveIndex (e.g. a full rebuild's first load) and resets
    // the overlay to empty. Serialized with compaction so an in-flight preparation cannot publish an
    // obsolete base after this replacement.
    public void SwapSnapshot(Snapshot newSnapshot)
    {
        lock (_mutationGate)
        {
            _lock.EnterWriteLock();
            try
            {
                EnsureUsable();
                SwapUnderLock(newSnapshot);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }

    // Caller must already hold the write lock. Safe to dispose the old mapping synchronously here:
    // the write lock is exclusive, so no Read() can be holding a reference into it concurrently.
    private void SwapUnderLock(Snapshot newSnapshot)
    {
        var old = _snapshot;
        _snapshot = newSnapshot;
        _delta = new DeltaOverlay(newSnapshot);
        Interlocked.Increment(ref _revision);
        old?.Dispose();
    }

    public void Dispose()
    {
        lock (_mutationGate)
        {
            _lock.EnterWriteLock();
            try
            {
                _snapshot?.Dispose();
                _snapshot = null;
                _delta = null;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            // Readers may still be queued while shutdown clears the index.
            LiveIndexLockTeardown.TryDispose(_lock);
        }
    }
}
