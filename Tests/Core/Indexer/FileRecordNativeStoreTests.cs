using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lertaro.Core.Tests.Indexer;

[TestClass]
public sealed class FileRecordNativeStoreTests
{
    [TestMethod]
    public void Collection_PreservesRowsNamesAndListOperations()
    {
        var highId = ((UInt128)0xFEDCBA9876543210UL << 64) | 0x0123456789ABCDEFUL;
        var parentId = ((UInt128)0x8000000000000001UL << 64) | 0x1000000000000002UL;
        var first = new FileRecord(highId, parentId, "中文😀.txt", FileRecordFlags.Hidden, 987654321,
            123, 456, 789);
        var second = new FileRecord(11, 12, "中文😀.txt", FileRecordFlags.Directory | FileRecordFlags.Listed,
            0, 10, 20, 30);
        var records = new FileRecordCollection();

        records.EnsureCapacity(8);
        records.Add(first);
        records.Add(second);
        Assert.AreEqual(2, records.Count);
        Assert.IsTrue(records.Capacity >= 8);
        Assert.AreEqual(first, records[0]);
        Assert.AreEqual(second, records[1]);
        Assert.AreEqual(records.GetNameIdAt(0), records.GetNameIdAt(1));

        var replacement = new FileRecord(second.Id, second.ParentId, "renamed", FileRecordFlags.Directory,
            0, 31, 32, 33);
        records[1] = replacement;
        Assert.AreEqual(replacement, records[1]);
        records.UpdateMetadata(1, FileRecordFlags.Directory | FileRecordFlags.Listed, 41, 42, 43);
        Assert.AreEqual(FileRecordFlags.Directory | FileRecordFlags.Listed, records[1].Flags);
        Assert.AreEqual(42u, records[1].LastWriteTimeUnixSeconds);

        records.Insert(1, first);
        Assert.AreEqual(first, records[1]);
        Assert.IsTrue(records.Remove(first));
        Assert.AreEqual(replacement.Id, records[1].Id);

        var copy = new FileRecordCollection();
        copy.AddRange(records);
        CollectionAssert.AreEqual(records.ToArray(), copy.ToArray());
        copy.AddRange(copy);
        Assert.AreEqual(4, copy.Count);
        CollectionAssert.AreEqual(new[] { records[0], records[1], records[0], records[1] }, copy.ToArray());

        copy.Clear();
        copy.TrimExcess();
        Assert.AreEqual(0, copy.Count);
        copy.Add(first);
        Assert.AreEqual(first, copy[0]);
    }

    [TestMethod]
    public void NativeRecordRow_IsSmallerThanManagedFileRecord()
    {
        Assert.AreEqual(48, Marshal.SizeOf<NativeRecordRow>());
        Assert.AreEqual(16, Marshal.SizeOf<NativeRecordHighPair>());
        Assert.IsGreaterThan(Marshal.SizeOf<NativeRecordRow>(), Unsafe.SizeOf<FileRecord>());
    }

    [TestMethod]
    public void Indexer_ReadsUnicodeNameFromNativeStore()
    {
        var expected = new FileRecord(1, 1, "目录😀.txt", FileRecordFlags.None);
        var records = new FileRecordCollection { expected };

        records.GetNameIdAt(0); // Flush the pending row so the indexer uses the native read path.

        Assert.AreEqual(expected, records[0]);
        Assert.AreEqual("目录😀.txt", records[0].Name);
    }

    [TestMethod]
    public void AddRange_ReusesNativeInputScratchAcrossBatchFlushAndClear()
    {
        var rows = Enumerable.Range(0, 513)
            .Select(i => new FileRecord((UInt128)(i + 1), 1,
                i == 0 ? new string('长', 16_384) : $"目录😀-{i}.txt", FileRecordFlags.None, i))
            .ToArray();
        var records = new FileRecordCollection();

        records.AddRange(rows);
        CollectionAssert.AreEqual(rows, records.ToArray());
        records.Clear();
        records.AddRange(rows.Take(512));

        CollectionAssert.AreEqual(rows.Take(512).ToArray(), records.ToArray());
    }

    [TestMethod]
    public void Enumeration_ThrowsWhenCollectionChanges()
    {
        var records = new FileRecordCollection
        {
            new FileRecord(1, 1, "first", FileRecordFlags.None),
            new FileRecord(2, 1, "second", FileRecordFlags.None),
        };
        using var enumerator = records.GetEnumerator();

        Assert.IsTrue(enumerator.MoveNext());
        records.Add(new FileRecord(3, 1, "third", FileRecordFlags.None));

        Assert.ThrowsExactly<InvalidOperationException>(() => enumerator.MoveNext());
    }

    [TestMethod]
    public void Dispose_ReleasesStoreAndPreventsReuse()
    {
        var records = new FileRecordCollection
        {
            new FileRecord(1, 1, "first", FileRecordFlags.None),
        };
        var lease = records.AcquireReadLease();

        Assert.ThrowsExactly<InvalidOperationException>(() => records.Dispose());
        lease.Dispose();

        records.Dispose();
        records.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => records.Add(new FileRecord(2, 1, "second", FileRecordFlags.None)));
        Assert.ThrowsExactly<ObjectDisposedException>(() => records.EnsureCapacity(8));
    }

    [TestMethod]
    public void FileRecordStore_DisposeReleasesRecords()
    {
        var store = new FileRecordStore();
        store.Records.Add(new FileRecord(1, 1, "first", FileRecordFlags.None));

        store.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => store.Records.Add(new FileRecord(2, 1, "second", FileRecordFlags.None)));
    }

    [TestMethod]
    public void AddRange_DeferredViewOfSelfStopsAfterPartialAppend()
    {
        var original = new FileRecord(1, 1, "first", FileRecordFlags.None);
        var records = new FileRecordCollection { original };

        Assert.ThrowsExactly<InvalidOperationException>(() => records.AddRange(records.Where(_ => true)));

        Assert.AreEqual(2, records.Count);
        Assert.AreEqual(original, records[0]);
        Assert.AreEqual(original, records[1]);
    }
}
