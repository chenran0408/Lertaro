using System.Runtime.InteropServices;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;

namespace Lertaro.Core;

internal static unsafe class NativeCompactionInterop
{
    internal static NativeSnapshotBuilder Build(Snapshot snapshot, DeltaOverlay delta,
        out string[] uniqueNames)
    {
        var baseNames = new string[snapshot.UniqueCount];
        for (var uid = 0; uid < baseNames.Length; uid++)
            baseNames[uid] = snapshot.GetUniqueName(uid);

        // Preserve the base UID table instead of looking up every unchanged row in a dictionary.
        // Compaction usually changes only a small fraction of rows, so this keeps the hot loop on
        // the native snapshot's existing integer IDs and only allocates IDs for changed names.
        var names = new List<string>(baseNames.Length + delta.VisibleAddedCount);
        var nameIds = new Dictionary<string, uint>(baseNames.Length + delta.VisibleAddedCount, StringComparer.Ordinal);
        for (var uid = 0; uid < baseNames.Length; uid++)
        {
            names.Add(baseNames[uid]);
            nameIds.Add(baseNames[uid], (uint)uid);
        }
        uint GetNameId(string name)
        {
            if (nameIds.TryGetValue(name, out var uid))
                return uid;
            uid = checked((uint)names.Count);
            nameIds.Add(name, uid);
            names.Add(name);
            return uid;
        }

        var actions = new int[snapshot.Count];
        Array.Fill(actions, -1);
        var baseNameIds = snapshot.NameIds.ToArray();
        var patches = new List<NativeCompactionPatch>(delta.BaseOverrides.Count + delta.MetadataOverrides.Count);

        for (var row = 0; row < snapshot.Count; row++)
        {
            if (delta.DeletedBase.Contains(row) || delta.RenamedAway.ContainsKey(row))
            {
                actions[row] = -2;
                continue;
            }

            if (delta.BaseOverrides.TryGetValue(row, out var overrideRecord))
            {
                baseNameIds[row] = GetNameId(overrideRecord.Name);
                actions[row] = patches.Count;
                patches.Add(NativeCompactionPatch.From(overrideRecord.ParentFrn, baseNameIds[row],
                    overrideRecord.Flags, overrideRecord.Size, overrideRecord.Creation,
                    overrideRecord.LastWrite, overrideRecord.LastAccess));
                continue;
            }

            if (delta.MetadataOverrides.TryGetValue(row, out var metadata))
            {
                actions[row] = patches.Count;
                patches.Add(NativeCompactionPatch.From(snapshot.GetParentId(row), baseNameIds[row],
                    (ushort)snapshot.Flags[row], metadata.Size, metadata.Creation,
                    metadata.LastWrite, metadata.LastAccess));
            }
        }

        var added = new List<NativeCompactionRecord>(delta.VisibleAddedCount);
        foreach (var record in delta.Added)
        {
            if (record.Removed)
                continue;
            added.Add(NativeCompactionRecord.From(record, GetNameId(record.Name)));
        }

        uniqueNames = names.ToArray();
        var baseIds = snapshot.Ids;
        var baseFlags = snapshot.Flags;
        var baseSizes = snapshot.Sizes;
        var baseCreationTimes = snapshot.CreationTimes;
        var baseLastWriteTimes = snapshot.LastWriteTimes;
        var baseLastAccessTimes = snapshot.LastAccessTimes;
        var baseParentIndexes = snapshot.ParentIndexes;
        var orphanRows = snapshot.OrphanRows;
        var orphanFrns = snapshot.OrphanFrns;
        var root = snapshot.RootId;

        fixed (UInt128* ids = baseIds)
        fixed (ushort* flags = baseFlags)
        fixed (long* sizes = baseSizes)
        fixed (uint* creation = baseCreationTimes)
        fixed (uint* lastWrite = baseLastWriteTimes)
        fixed (uint* lastAccess = baseLastAccessTimes)
        fixed (int* parentIndexes = baseParentIndexes)
        fixed (int* orphanRowPointer = orphanRows)
        fixed (UInt128* orphanFrnPointer = orphanFrns)
        fixed (uint* nameIdPointer = baseNameIds)
        fixed (int* actionPointer = actions)
        fixed (NativeCompactionPatch* patchPointer = CollectionsMarshal.AsSpan(patches))
        fixed (NativeCompactionRecord* addedPointer = CollectionsMarshal.AsSpan(added))
        {
            FileRecordCollection.ThrowOnNativeFailure(Native.CompactionSnapshotNew(
                ids, nameIdPointer, flags, sizes, creation, lastWrite, lastAccess, parentIndexes,
                orphanRowPointer, orphanFrnPointer, actionPointer, patchPointer, patches.Count,
                addedPointer, added.Count, snapshot.Count, orphanRows.Length, names.Count,
                (ulong)root, (ulong)(root >> 64), out var handle));
            return new NativeSnapshotBuilder(handle);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCompactionPatch
{
    internal ulong ParentLow, ParentHigh;
    internal uint NameUid;
    internal ushort Flags, Reserved;
    internal long Size;
    internal uint CreationTime, LastWriteTime, LastAccessTime;

    internal static NativeCompactionPatch From(UInt128 parent, uint nameUid, ushort flags, long size,
        uint creation, uint lastWrite, uint lastAccess) => new()
    {
        ParentLow = (ulong)parent,
        ParentHigh = (ulong)(parent >> 64),
        NameUid = nameUid,
        Flags = flags,
        Size = size,
        CreationTime = creation,
        LastWriteTime = lastWrite,
        LastAccessTime = lastAccess,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCompactionRecord
{
    internal ulong IdLow, IdHigh, ParentLow, ParentHigh;
    internal uint NameUid;
    internal ushort Flags, Reserved;
    internal long Size;
    internal uint CreationTime, LastWriteTime, LastAccessTime;

    internal static NativeCompactionRecord From(DeltaOverlay.DeltaRecord record, uint nameUid) => new()
    {
        IdLow = (ulong)record.Id,
        IdHigh = (ulong)(record.Id >> 64),
        ParentLow = (ulong)record.ParentFrn,
        ParentHigh = (ulong)(record.ParentFrn >> 64),
        NameUid = nameUid,
        Flags = record.Flags,
        Size = record.Size,
        CreationTime = record.Creation,
        LastWriteTime = record.LastWrite,
        LastAccessTime = record.LastAccess,
    };
}
