using System.Diagnostics;
using System.Text;
using Lertaro.Core.SearchIndex.Fzf;

const int warmups = 2;
const int iterations = 5;
var sizes = new[] { 100_000, 500_000, 1_000_000 };
var asciiQueries = new[] { "a", "proj", "read", "abc", "xyz" };
var unicodeQueries = new[] { "项", "搜索", "说明", "项目", "xyz" };

Console.WriteLine("Lertaro fuzzy-search baseline");
Console.WriteLine($"runtime={Environment.Version} cpu={Environment.ProcessorCount}");
Console.WriteLine("dataset,kind,query,size,matched,avg_ms,allocated_bytes");

foreach (var size in sizes)
{
    var ascii = new string[size];
    var unicode = new string[size];
    var asciiBytes = new byte[size][];
    for (var i = 0; i < size; i++)
    {
        var suffix = i.ToString("D7");
        ascii[i] = $"Project_{i % 23:D2}_Searchable_{suffix}_Readme.txt";
        unicode[i] = i % 17 == 0
            ? $"项目_{i % 23:D2}_搜索_{suffix}_说明.txt"
            : $"项目_{i % 23:D2}_缓存_{suffix}_资料.txt";
        asciiBytes[i] = Encoding.ASCII.GetBytes(ascii[i]);
    }

    foreach (var query in asciiQueries)
    {
        BenchmarkAscii(asciiBytes, query, size);
        BenchmarkAsciiParallel(asciiBytes, query, size);
    }
    foreach (var query in unicodeQueries)
        BenchmarkUnicode(unicode, query, size);
}

static void BenchmarkAscii(byte[][] names, string query, int size)
{
    var pattern = FzfBytePattern.From(FzfPattern.Parse(query));
    var slab = new FzfSlab();
    var buffers = new FzfByteBuffers();
    var matched = 0;
    for (var i = 0; i < warmups; i++)
        matched = ScanAscii(names, pattern, slab, buffers);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
        matched = ScanAscii(names, pattern, slab, buffers);
    timer.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"synthetic,ascii,{query},{size},{matched},{timer.Elapsed.TotalMilliseconds / iterations:F3},{allocated / iterations}");
}

static int ScanAscii(byte[][] names, FzfBytePattern pattern, FzfSlab slab, FzfByteBuffers buffers)
{
    var matched = 0;
    foreach (var name in names)
        if (pattern.TryMatch(name, out _, FzfScoringScheme.Default, slab, buffers))
            matched++;
    return matched;
}


static void BenchmarkAsciiParallel(byte[][] names, string query, int size)
{
    var pattern = FzfBytePattern.From(FzfPattern.Parse(query));
    var matched = 0;
    for (var i = 0; i < warmups; i++)
        matched = ScanAsciiParallel(names, pattern);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
        matched = ScanAsciiParallel(names, pattern);
    timer.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"synthetic,ascii-parallel,{query},{size},{matched},{timer.Elapsed.TotalMilliseconds / iterations:F3},{allocated / iterations}");
}

static int ScanAsciiParallel(byte[][] names, FzfBytePattern pattern)
{
    var total = 0;
    Parallel.For(0, names.Length,
        () => (Count: 0, Slab: new FzfSlab(), Buffers: new FzfByteBuffers()),
        (i, _, local) =>
        {
            if (pattern.TryMatch(names[i], out var ignored, FzfScoringScheme.Default, local.Slab, local.Buffers))
                local.Count++;
            return local;
        },
        local => Interlocked.Add(ref total, local.Count));
    return total;
}


static void BenchmarkUnicode(string[] names, string query, int size)
{
    var pattern = FzfPattern.Parse(query);
    var slab = new FzfSlab();
    var matched = 0;
    for (var i = 0; i < warmups; i++)
        matched = ScanUnicode(names, pattern, slab);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var before = GC.GetAllocatedBytesForCurrentThread();
    var timer = Stopwatch.StartNew();
    for (var i = 0; i < iterations; i++)
        matched = ScanUnicode(names, pattern, slab);
    timer.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"synthetic,unicode,{query},{size},{matched},{timer.Elapsed.TotalMilliseconds / iterations:F3},{allocated / iterations}");
}

static int ScanUnicode(string[] names, FzfPattern pattern, FzfSlab slab)
{
    var matched = 0;
    foreach (var name in names)
        if (pattern.TryMatch(name, out _, FzfScoringScheme.Default, slab))
            matched++;
    return matched;
}

