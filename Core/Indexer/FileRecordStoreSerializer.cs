namespace Lertaro.Core;

internal sealed class FileRecordNamePool
{
    // Name interning now happens in FileRecordCollection's Rust builder; keeping a parallel managed
    // dictionary here would retain a second copy of every unique name while large drives are scanned.
    public string Get(string value) => string.IsNullOrEmpty(value) ? string.Empty : value;
}

public static class FileRecordStoreSerializer
{
    public static string GetBasePath(string cacheDir, string sourceKey) => Path.Combine(cacheDir, sourceKey.ToLowerInvariant());
}
