using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core;

public sealed partial class FileRecordCollection
{
    public bool Contains(FileRecord item) => IndexOf(item) >= 0;

    public int IndexOf(FileRecord item)
    {
        for (var i = 0; i < Count; i++)
            if (this[i].Equals(item))
                return i;
        return -1;
    }

    public bool Remove(FileRecord item)
    {
        var index = IndexOf(item);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }

    public void CopyTo(FileRecord[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if ((uint)arrayIndex > (uint)array.Length || array.Length - arrayIndex < Count)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        for (var i = 0; i < Count; i++)
            array[arrayIndex + i] = this[i];
    }

    internal RecordReadLease AcquireReadLease(bool sortActive = false)
    {
        Monitor.Enter(_gate);
        try
        {
            ThrowIfDisposed();
            FlushPendingLocked();
            ThrowOnNativeFailure(Native.RecordStoreView(_handle, out var rows, out var rowCount,
                out var highPairs, out var highPairCount));
            ThrowOnNativeFailure(Native.RecordStoreNameView(_handle, out var nameSpans, out var nameSpanCount,
                out var nameArena, out var nameArenaCount));
            var order = IntPtr.Zero;
            var orderCount = 0;
            if (sortActive)
                ThrowOnNativeFailure(Native.RecordStoreSortActive(_handle, out order, out orderCount));
            var lease = new RecordReadLease(this, rows, rowCount, highPairs, highPairCount,
                nameSpans, nameSpanCount, nameArena, nameArenaCount, order, orderCount, sortActive);
            _activeLeases++;
            return lease;
        }
        catch
        {
            Monitor.Exit(_gate);
            throw;
        }
    }

    private NativeRecordOutput GetDataLocked(int index)
    {
        CheckIndex(index);
        ThrowOnNativeFailure(Native.RecordStoreGet(_handle, index, out var output, out _, out _));
        return output;
    }

    private NativeRecordOutput GetDataAndNameLocked(int index, out string name)
    {
        CheckIndex(index);
        ThrowOnNativeFailure(Native.RecordStoreGet(_handle, index, out var output, out var pointer, out var length));
        GC.KeepAlive(_handle);
        name = length == 0 ? string.Empty : Marshal.PtrToStringUni(pointer, length)!;
        return output;
    }

    private string GetNameLocked(uint nameId)
    {
        ThrowOnNativeFailure(Native.RecordStoreGetName(_handle, nameId, out var pointer, out var length));
        GC.KeepAlive(_handle);
        return length == 0 ? string.Empty : Marshal.PtrToStringUni(pointer, length)!;
    }

    private void CheckIndex(int index)
    {
        if ((uint)index >= (uint)_count)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    internal static void ThrowOnNativeFailure(int result)
    {
        if (result != 0)
            throw new InvalidOperationException($"The native file-record store failed with status {result}.");
    }

    internal ref struct RecordReadLease
    {
        private readonly FileRecordCollection _owner;
        private readonly bool _hasSortedIndexes;
        private readonly ReadLeaseToken _token;

        internal unsafe RecordReadLease(FileRecordCollection owner, IntPtr rows, int rowCount, IntPtr highPairs,
            int highPairCount, IntPtr nameSpans, int nameSpanCount, IntPtr nameArena, int nameArenaCount,
            IntPtr order, int orderCount, bool hasSortedIndexes)
        {
            _owner = owner;
            _hasSortedIndexes = hasSortedIndexes;
            _token = new ReadLeaseToken();
            Rows = new ReadOnlySpan<NativeRecordRow>((void*)rows, rowCount);
            HighPairs = new ReadOnlySpan<NativeRecordHighPair>((void*)highPairs, highPairCount);
            NameSpans = new ReadOnlySpan<NativeRecordNameSpan>((void*)nameSpans, nameSpanCount);
            NameArena = new ReadOnlySpan<char>((void*)nameArena, nameArenaCount);
            SortedActiveIndexes = new ReadOnlySpan<int>((void*)order, orderCount);
        }

        internal ReadOnlySpan<NativeRecordRow> Rows { get; }
        internal ReadOnlySpan<NativeRecordHighPair> HighPairs { get; }
        internal ReadOnlySpan<NativeRecordNameSpan> NameSpans { get; }
        internal ReadOnlySpan<char> NameArena { get; }
        internal ReadOnlySpan<int> SortedActiveIndexes { get; }

        internal NativeSnapshotBuilder BuildSnapshot(UInt128 rootId)
        {
            ThrowOnNativeFailure(Native.SnapshotBuilderNew(_owner._handle, (ulong)rootId,
                (ulong)(rootId >> 64), out var handle));
            var builder = new NativeSnapshotBuilder(handle);
            ThrowOnNativeFailure(Native.RecordStoreReleaseSorted(_owner._handle));
            return builder;
        }

        internal string GetName(uint nameId)
        {
            var span = NameSpans[(int)nameId];
            return span.Length == 0
                ? string.Empty
                : new string(NameArena.Slice(checked((int)span.Offset), checked((int)span.Length)));
        }

        public void Dispose()
        {
            if (_token is null || Interlocked.Exchange(ref _token.Disposed, 1) != 0)
                return;
            try
            {
                if (_hasSortedIndexes)
                    ThrowOnNativeFailure(Native.RecordStoreReleaseSorted(_owner._handle));
            }
            finally
            {
                GC.KeepAlive(_owner._handle);
                _owner._activeLeases--;
                Monitor.Exit(_owner._gate);
            }
        }
    }

    private sealed class ReadLeaseToken
    {
        internal int Disposed;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRecordRow
{
    internal ulong IdLow, ParentLow;
    internal long Size;
    internal uint CreationTime, LastWriteTime, LastAccessTime;
    internal uint HighPairIndex, NameId;
    internal ushort Flags, Reserved;

    internal UInt128 GetId(ReadOnlySpan<NativeRecordHighPair> highPairs)
        => new(HighPairIndex == uint.MaxValue ? 0 : highPairs[(int)HighPairIndex].IdHigh, IdLow);
    internal UInt128 GetParentId(ReadOnlySpan<NativeRecordHighPair> highPairs)
        => new(HighPairIndex == uint.MaxValue ? 0 : highPairs[(int)HighPairIndex].ParentHigh, ParentLow);
    internal FileRecordFlags RecordFlags => (FileRecordFlags)Flags;
    internal bool IsDeleted => (RecordFlags & FileRecordFlags.Deleted) != 0;
    internal long LogicalSize => Size;
    internal uint CreationTimeUnixSeconds => CreationTime;
    internal uint LastWriteTimeUnixSeconds => LastWriteTime;
    internal uint LastAccessTimeUnixSeconds => LastAccessTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRecordHighPair
{
    internal ulong IdHigh, ParentHigh;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct NativeRecordNameSpan
{
    internal ulong Offset;
    internal uint Length;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRecordInput
{
    internal ulong IdLow, IdHigh, ParentLow, ParentHigh;
    internal int NameOffset, NameLength;
    internal ushort Flags, Reserved;
    internal long Size;
    internal uint CreationTime, LastWriteTime, LastAccessTime;

    internal static NativeRecordInput From(FileRecord record, int nameOffset, int nameLength) => new()
    {
        IdLow = (ulong)record.Id,
        IdHigh = (ulong)(record.Id >> 64),
        ParentLow = (ulong)record.ParentId,
        ParentHigh = (ulong)(record.ParentId >> 64),
        NameOffset = nameOffset,
        NameLength = nameLength,
        Flags = (ushort)record.Flags,
        Size = record.Size,
        CreationTime = record.CreationTimeUnixSeconds,
        LastWriteTime = record.LastWriteTimeUnixSeconds,
        LastAccessTime = record.LastAccessTimeUnixSeconds,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRecordOutput
{
    internal ulong IdLow, IdHigh, ParentLow, ParentHigh;
    internal uint NameId;
    internal ushort Flags, Reserved;
    internal long Size;
    internal uint CreationTime, LastWriteTime, LastAccessTime;

    internal UInt128 Id => new(IdHigh, IdLow);
    internal UInt128 ParentId => new(ParentHigh, ParentLow);
    internal FileRecordFlags RecordFlags => (FileRecordFlags)Flags;
    internal bool IsDeleted => (RecordFlags & FileRecordFlags.Deleted) != 0;
    internal long LogicalSize => Size;
    internal uint CreationTimeUnixSeconds => CreationTime;
    internal uint LastWriteTimeUnixSeconds => LastWriteTime;
    internal uint LastAccessTimeUnixSeconds => LastAccessTime;

    internal static NativeRecordOutput From(FileRecord record) => new()
    {
        IdLow = (ulong)record.Id,
        IdHigh = (ulong)(record.Id >> 64),
        ParentLow = (ulong)record.ParentId,
        ParentHigh = (ulong)(record.ParentId >> 64),
        Flags = (ushort)record.Flags,
        Size = record.Size,
        CreationTime = record.CreationTimeUnixSeconds,
        LastWriteTime = record.LastWriteTimeUnixSeconds,
        LastAccessTime = record.LastAccessTimeUnixSeconds,
    };
}

internal sealed class NativeStoreHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal NativeStoreHandle() : base(ownsHandle: true)
    {
        SetHandle(Native.RecordStoreNew());
        if (IsInvalid)
            throw new OutOfMemoryException("Could not create the native file-record store.");
    }

    protected override bool ReleaseHandle()
    {
        Native.RecordStoreFree(handle);
        return true;
    }
}

internal sealed unsafe class NativeSnapshotBuilder : IDisposable
{
    private readonly SnapshotBuilderHandle _handle;
    private readonly NativeSnapshotView _view;

    internal NativeSnapshotBuilder(SnapshotBuilderHandle handle)
    {
        _handle = handle;
        FileRecordCollection.ThrowOnNativeFailure(Native.SnapshotBuilderView(handle, out _view));
    }

    internal int Count => _view.RowCount;
    internal int UniqueCount => _view.UniqueCount;
    internal int ChildrenCount => _view.ChildrenCount;
    internal int OrphanCount => _view.OrphanCount;
    internal int TotalFiles => _view.TotalFiles;
    internal int TotalDirs => _view.TotalDirs;
    internal ReadOnlySpan<uint> NameIds => new((void*)_view.NameIds, Count);
    internal ReadOnlySpan<ushort> Flags => new((void*)_view.Flags, Count);
    internal ReadOnlySpan<int> ParentIndexes => new((void*)_view.ParentIndexes, Count);
    internal ReadOnlySpan<UInt128> Ids => new((void*)_view.Ids, Count);
    internal ReadOnlySpan<long> Sizes => new((void*)_view.Sizes, Count);
    internal ReadOnlySpan<long> RecursiveSizes => new((void*)_view.RecursiveSizes, Count);
    internal ReadOnlySpan<uint> CreationTimes => new((void*)_view.CreationTimes, Count);
    internal ReadOnlySpan<uint> LastWriteTimes => new((void*)_view.LastWriteTimes, Count);
    internal ReadOnlySpan<uint> LastAccessTimes => new((void*)_view.LastAccessTimes, Count);
    internal ReadOnlySpan<int> ChildStarts => new((void*)_view.ChildStarts, Count + 1);
    internal ReadOnlySpan<int> Children => new((void*)_view.Children, ChildrenCount);
    internal ReadOnlySpan<int> UidStarts => new((void*)_view.UidStarts, UniqueCount + 1);
    internal ReadOnlySpan<int> UidRows => new((void*)_view.UidRows, Count);
    internal ReadOnlySpan<int> OrphanRows => new((void*)_view.OrphanRows, OrphanCount);
    internal ReadOnlySpan<UInt128> OrphanFrns => new((void*)_view.OrphanFrns, OrphanCount);
    internal ReadOnlySpan<uint> UniqueNameIds => new((void*)_view.UniqueNameIds, UniqueCount);

    public void Dispose() => _handle.Dispose();
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSnapshotView
{
    internal IntPtr NameIds, Flags, ParentIndexes, Ids, Sizes, RecursiveSizes;
    internal IntPtr CreationTimes, LastWriteTimes, LastAccessTimes;
    internal IntPtr ChildStarts, Children, UidStarts, UidRows, OrphanRows, OrphanFrns, UniqueNameIds;
    internal int RowCount, UniqueCount, ChildrenCount, OrphanCount, TotalFiles, TotalDirs;
}

internal sealed class SnapshotBuilderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SnapshotBuilderHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        Native.SnapshotBuilderFree(handle);
        return true;
    }
}

internal static class Native
{
    private const string Library = "lertaro_index_store";

    [DllImport(Library, EntryPoint = "lertaro_record_store_new", CallingConvention = CallingConvention.Winapi)]
    internal static extern IntPtr RecordStoreNew();
    [DllImport(Library, EntryPoint = "lertaro_record_store_free", CallingConvention = CallingConvention.Winapi)]
    internal static extern void RecordStoreFree(IntPtr handle);
    [DllImport(Library, EntryPoint = "lertaro_record_store_capacity", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreCapacity(NativeStoreHandle handle);
    [DllImport(Library, EntryPoint = "lertaro_record_store_reserve", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreReserve(NativeStoreHandle handle, int capacity);
    [DllImport(Library, EntryPoint = "lertaro_record_store_set_capacity", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreSetCapacity(NativeStoreHandle handle, int capacity);
    [DllImport(Library, EntryPoint = "lertaro_record_store_clear", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreClear(NativeStoreHandle handle);
    [DllImport(Library, EntryPoint = "lertaro_record_store_trim", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreTrim(NativeStoreHandle handle);
    [DllImport(Library, EntryPoint = "lertaro_record_store_add_batch", CallingConvention = CallingConvention.Winapi)]
    internal static extern unsafe int RecordStoreAddBatch(NativeStoreHandle handle, NativeRecordInput* records,
        int count, char* names, int nameCount);
    [DllImport(Library, EntryPoint = "lertaro_record_store_get", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreGet(NativeStoreHandle handle, int index, out NativeRecordOutput record,
        out IntPtr name, out int nameLength);
    [DllImport(Library, EntryPoint = "lertaro_record_store_view", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreView(NativeStoreHandle handle, out IntPtr rows, out int count,
        out IntPtr highPairs, out int highPairCount);
    [DllImport(Library, EntryPoint = "lertaro_record_store_name_view", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreNameView(NativeStoreHandle handle, out IntPtr spans, out int spanCount,
        out IntPtr arena, out int arenaCount);
    [DllImport(Library, EntryPoint = "lertaro_record_store_sort_active", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreSortActive(NativeStoreHandle handle, out IntPtr order, out int count);
    [DllImport(Library, EntryPoint = "lertaro_record_store_release_sorted", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreReleaseSorted(NativeStoreHandle handle);
    [DllImport(Library, EntryPoint = "lertaro_snapshot_builder_new", CallingConvention = CallingConvention.Winapi)]
    internal static extern int SnapshotBuilderNew(NativeStoreHandle handle, ulong rootLow, ulong rootHigh,
        out SnapshotBuilderHandle builder);
    [DllImport(Library, EntryPoint = "lertaro_compaction_snapshot_new", CallingConvention = CallingConvention.Winapi)]
    internal static extern unsafe int CompactionSnapshotNew(
        UInt128* baseIds, uint* baseNameUids, ushort* baseFlags, long* baseSizes,
        uint* baseCreationTimes, uint* baseLastWriteTimes, uint* baseLastAccessTimes,
        int* baseParentIndexes, int* orphanRows, UInt128* orphanFrns, int* actions,
        NativeCompactionPatch* patches, int patchCount, NativeCompactionRecord* added, int addedCount,
        int baseCount, int orphanCount, int uniqueCount, ulong rootLow, ulong rootHigh,
        out SnapshotBuilderHandle builder);
    [DllImport(Library, EntryPoint = "lertaro_snapshot_builder_view", CallingConvention = CallingConvention.Winapi)]
    internal static extern int SnapshotBuilderView(SnapshotBuilderHandle handle, out NativeSnapshotView view);
    [DllImport(Library, EntryPoint = "lertaro_snapshot_builder_free", CallingConvention = CallingConvention.Winapi)]
    internal static extern void SnapshotBuilderFree(IntPtr handle);
    [DllImport(Library, EntryPoint = "lertaro_record_store_get_name", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreGetName(NativeStoreHandle handle, uint nameId, out IntPtr name, out int length);
    [DllImport(Library, EntryPoint = "lertaro_record_store_set", CallingConvention = CallingConvention.Winapi)]
    internal static extern unsafe int RecordStoreSet(NativeStoreHandle handle, int index, ref NativeRecordInput record, char* name);
    [DllImport(Library, EntryPoint = "lertaro_record_store_insert", CallingConvention = CallingConvention.Winapi)]
    internal static extern unsafe int RecordStoreInsert(NativeStoreHandle handle, int index, ref NativeRecordInput record, char* name);
    [DllImport(Library, EntryPoint = "lertaro_record_store_remove_at", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreRemoveAt(NativeStoreHandle handle, int index);
    [DllImport(Library, EntryPoint = "lertaro_record_store_update_metadata", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreUpdateMetadata(NativeStoreHandle handle, int index, ushort flags,
        uint creationTime, uint lastWriteTime, uint lastAccessTime);
    [DllImport(Library, EntryPoint = "lertaro_record_store_extend", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreExtend(NativeStoreHandle handle, NativeStoreHandle source, int start, int count);
    [DllImport(Library, EntryPoint = "lertaro_record_store_clone", CallingConvention = CallingConvention.Winapi)]
    internal static extern int RecordStoreClone(NativeStoreHandle handle, NativeStoreHandle source, out int count);
}
