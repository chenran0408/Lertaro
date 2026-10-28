using System.Collections;

namespace Lertaro.Core;

// Build-time records live in Rust as a 48-byte row plus sparse high-ID pairs and unique UTF-16 names.
// The snapshot remains the existing v10 memory-mapped file; this type is only its mutable builder.
public sealed partial class FileRecordCollection : IList<FileRecord>, IReadOnlyList<FileRecord>, IDisposable
{
    private const int BatchSize = 512;
    private readonly object _gate = new();
    private readonly NativeStoreHandle _handle;
    private readonly List<FileRecord> _pending = new(BatchSize);
    private int _count;
    private int _flushedCount;
    private int _version;
    private int _disposed;
    private int _activeLeases;

    public FileRecordCollection() => _handle = new NativeStoreHandle();

    public int Count { get { ThrowIfDisposed(); return Volatile.Read(ref _count); } }
    public bool IsReadOnly => false;

    public int Capacity
    {
        get { lock (_gate) { ThrowIfDisposed(); return Math.Max(Native.RecordStoreCapacity(_handle), _count); } }
        set
        {
            ThrowIfDisposed();
            if (value < _count)
                throw new ArgumentOutOfRangeException(nameof(value), "Capacity cannot be less than Count.");
            lock (_gate)
            {
                ThrowIfDisposed();
                FlushPendingLocked();
                ThrowOnNativeFailure(Native.RecordStoreSetCapacity(_handle, value));
            }
        }
    }

    public FileRecord this[int index]
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                CheckIndex(index);
                if (index >= _flushedCount)
                    return _pending[index - _flushedCount];
                var row = GetDataAndNameLocked(index, out var name);
                return new FileRecord(row.Id, row.ParentId, name, row.RecordFlags, row.LogicalSize,
                    row.CreationTimeUnixSeconds, row.LastWriteTimeUnixSeconds, row.LastAccessTimeUnixSeconds);
            }
        }
        set
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                CheckIndex(index);
                if (index >= _flushedCount)
                {
                    _pending[index - _flushedCount] = value;
                    Interlocked.Increment(ref _version);
                    return;
                }
                var name = value.Name ?? string.Empty;
                var input = NativeRecordInput.From(value, 0, name.Length);
                unsafe
                {
                    fixed (char* namePointer = name)
                        ThrowOnNativeFailure(Native.RecordStoreSet(_handle, index, ref input, namePointer));
                }
                Interlocked.Increment(ref _version);
            }
        }
    }

    public void Add(FileRecord item)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            AppendPendingLocked(item);
        }
    }

    public void AddRange(IEnumerable<FileRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records is FileRecordCollection nativeRecords)
        {
            ExtendFrom(nativeRecords);
            return;
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var record in records)
                AppendPendingLocked(record);
        }
    }

    private void ExtendFrom(FileRecordCollection source)
    {
        if (ReferenceEquals(source, this))
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                FlushPendingLocked();
                ExtendRangeLocked(0, _count);
            }
            return;
        }

        source.FlushPending();
        var sourceCount = source.Count;
        lock (_gate)
        {
            ThrowIfDisposed();
            FlushPendingLocked();
            for (var start = 0; start < sourceCount; start += BatchSize)
                ExtendRangeLocked(source, start, Math.Min(BatchSize, sourceCount - start));
        }
    }

    private void ExtendRangeLocked(int start, int count)
        => ExtendRangeLocked(this, start, count);

    private void ExtendRangeLocked(FileRecordCollection source, int start, int count)
    {
        ThrowOnNativeFailure(Native.RecordStoreExtend(_handle, source._handle, start, count));
        _count = checked(_count + count);
        _flushedCount = checked(_flushedCount + count);
        if (count != 0)
            Interlocked.Increment(ref _version);
    }

    private void AppendPendingLocked(FileRecord record)
    {
        _pending.Add(record);
        _count++;
        Interlocked.Increment(ref _version);
        if (_pending.Count == BatchSize)
            FlushPendingLocked();
    }

    private void FlushPending()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            FlushPendingLocked();
        }
    }

    private void FlushPendingLocked()
    {
        if (_pending.Count == 0)
            return;

        var rows = new NativeRecordInput[_pending.Count];
        var names = new List<char>(_pending.Count * 24);
        for (var i = 0; i < _pending.Count; i++)
        {
            var record = _pending[i];
            var name = record.Name ?? string.Empty;
            var offset = names.Count;
            names.AddRange(name);
            rows[i] = NativeRecordInput.From(record, offset, name.Length);
        }
        var nameArray = names.ToArray();
        unsafe
        {
            fixed (NativeRecordInput* rowPointer = rows)
            fixed (char* namePointer = nameArray)
                ThrowOnNativeFailure(Native.RecordStoreAddBatch(_handle, rowPointer, rows.Length, namePointer, nameArray.Length));
        }
        _flushedCount += _pending.Count;
        _pending.Clear();
    }

    public int EnsureCapacity(int minimum)
    {
        if (minimum < 0)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowOnNativeFailure(Native.RecordStoreReserve(_handle, minimum));
            return Math.Max(Native.RecordStoreCapacity(_handle), Count);
        }
    }

    public void TrimExcess()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            FlushPendingLocked();
            ThrowOnNativeFailure(Native.RecordStoreTrim(_handle));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowOnNativeFailure(Native.RecordStoreClear(_handle));
            _pending.Clear();
            Volatile.Write(ref _count, 0);
            _flushedCount = 0;
            Interlocked.Increment(ref _version);
        }
    }

    public void Insert(int index, FileRecord item)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            FlushPendingLocked();
            if ((uint)index > (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            var name = item.Name ?? string.Empty;
            var input = NativeRecordInput.From(item, 0, name.Length);
            unsafe
            {
                fixed (char* namePointer = name)
                    ThrowOnNativeFailure(Native.RecordStoreInsert(_handle, index, ref input, namePointer));
            }
            _count++;
            _flushedCount++;
            Interlocked.Increment(ref _version);
        }
    }

    public void RemoveAt(int index)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            CheckIndex(index);
            FlushPendingLocked();
            ThrowOnNativeFailure(Native.RecordStoreRemoveAt(_handle, index));
            _count--;
            _flushedCount--;
            Interlocked.Increment(ref _version);
        }
    }

    internal NativeRecordOutput GetData(int index)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            CheckIndex(index);
            if (index >= _flushedCount)
            {
                var record = _pending[index - _flushedCount];
                return NativeRecordOutput.From(record);
            }
            return GetDataLocked(index);
        }
    }

    internal uint GetNameIdAt(int index)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            FlushPendingLocked();
            return GetDataLocked(index).NameId;
        }
    }

    internal string GetNameById(uint nameId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            FlushPendingLocked();
            return GetNameLocked(nameId);
        }
    }

    internal void UpdateMetadata(int index, FileRecordFlags flags, uint creationTime, uint lastWriteTime, uint lastAccessTime)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            CheckIndex(index);
            if (index >= _flushedCount)
            {
                var pendingIndex = index - _flushedCount;
                var current = _pending[pendingIndex];
                _pending[pendingIndex] = new FileRecord(current.Id, current.ParentId, current.Name,
                    flags, current.Size, creationTime, lastWriteTime, lastAccessTime);
                Interlocked.Increment(ref _version);
                return;
            }
            ThrowOnNativeFailure(Native.RecordStoreUpdateMetadata(_handle, index, (ushort)flags,
                creationTime, lastWriteTime, lastAccessTime));
            Interlocked.Increment(ref _version);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;
            if (_activeLeases != 0)
                throw new InvalidOperationException("Cannot dispose the file-record collection while a read lease is active.");
            Volatile.Write(ref _disposed, 1);
            _pending.Clear();
            Interlocked.Increment(ref _version);
            _handle.Dispose();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

}
