# ADR 0003: USN Snapshot Index

## Status

Accepted (recorded retrospectively; this describes the design as it exists in the source).

## Context

Local NTFS and ReFS drives need a search index that stays current in real time and survives
restarts without a full rescan. The USN change journal provides the change feed; a per-drive
index file provides durability. The two must stay consistent with each other across crashes,
journal deletion, and drive removal.

## Decision

- Each journal drive gets an immutable on-disk Snapshot plus an in-memory DeltaOverlay owned by a
  `LiveIndex`; searches read locklessly, mutation batches apply atomically
  ([Core/IndexV2/LiveIndex.cs:4](../../Core/IndexV2/LiveIndex.cs#L4)).
- The monitor reads the journal with `FSCTL_READ_USN_JOURNAL` — V0/V2 records on NTFS, V3 records
  with 128-bit IDs on ReFS ([Core/DriveMonitoring/UsnJournalRead.cs:32](../../Core/DriveMonitoring/UsnJournalRead.cs#L32)) —
  and advances the durable watermark (`JournalId`, `NextUsn`) only after a batch is actually applied
  ([Core/DriveMonitoring/UsnMonitor.cs:182](../../Core/DriveMonitoring/UsnMonitor.cs#L182)).
- Gap recovery is explicit, never silent: unreadable records queue a full rebuild
  ([Core/DriveMonitoring/UsnMonitor.cs:194](../../Core/DriveMonitoring/UsnMonitor.cs#L194));
  journal invalidation errors, a changed journal ID, or a watermark outside the journal's valid
  range stop the monitor and trigger a re-index
  ([Core/DriveMonitoring/UsnMonitor.cs:253](../../Core/DriveMonitoring/UsnMonitor.cs#L253));
  a journal recreated mid-session pins the watermark at the last consistent position
  ([Core/Indexer/Usn/UsnIndexerDurabilityExtensions.cs:39](../../Core/Indexer/Usn/UsnIndexerDurabilityExtensions.cs#L39)).
  A not-ready drive is reopened up to 30 times before giving up
  ([Core/DriveMonitoring/UsnMonitor.cs:272](../../Core/DriveMonitoring/UsnMonitor.cs#L272)).
- Durability comes from idle compaction: once a drive's pending delta passes 4096 changes,
  `LiveIndex.Compact` folds base snapshot + delta into a fresh snapshot stamped with the watermark,
  which is read before compaction so persisted content can only be newer than the stamp
  ([Core/Indexer/Usn/UsnIndexerDurabilityExtensions.cs:95](../../Core/Indexer/Usn/UsnIndexerDurabilityExtensions.cs#L95),
  [Core/IndexV2/LiveIndex.cs:103](../../Core/IndexV2/LiveIndex.cs#L103)). The writer serializes
  per target path, writes a temp file, flushes to disk, then replaces with retry
  ([Core/IndexV2/Persistence/SnapshotWriter.cs:251](../../Core/IndexV2/Persistence/SnapshotWriter.cs#L251),
  [Core/IndexV2/Persistence/SnapshotWriter.cs:287](../../Core/IndexV2/Persistence/SnapshotWriter.cs#L287)).

## Consequences

A cold start replays only the journal range since the last stamped snapshot; a batch that failed
to land replays from the durable watermark. Gaps that cannot be replayed become explicit full
rebuilds instead of silent index holes.
