using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Lertaro.Plugins.ClipboardHistory;

/// <summary>
/// A new capture handed to <see cref="ClipboardStore.AddAsync"/> before it has a row id. The store
/// derives dedup identity from it per kind: text hashes its content, a file list hashes the
/// case-insensitively normalized path set (NTFS paths are case-insensitive, so "C:\A" and "c:\a"
/// are the same copy), and an image arrives with the SHA-256 of its PNG bytes already computed by
/// the capture layer -- the same hex doubles as the content-addressed file name on disk.
/// </summary>
internal sealed record ClipboardEntryDraft(
    ClipboardEntryKind Kind,
    string Content,
    string ImagePath = "",
    int ImageWidth = 0,
    int ImageHeight = 0,
    string ImageHashHex = "",
    string Html = "")
{
    public byte[] ComputeHash()
    {
        var seed = Kind switch
        {
            ClipboardEntryKind.Text => Encoding.UTF8.GetBytes(Content),
            ClipboardEntryKind.FilePaths => Encoding.UTF8.GetBytes(JoinPaths(ToPathList(Content))),
            _ => Convert.FromHexString(ImageHashHex),
        };
        return SHA256.HashData(seed);
    }

    /// <summary>The normalized, case-insensitive file-list identity: paths uppercased and sorted, so
    /// a re-copy of the same folder contents in a different order still dedups.</summary>
    public static string JoinPaths(IEnumerable<string> paths) =>
        string.Join("\n", paths.Select(p => p.Trim().TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()).Order(StringComparer.Ordinal));

    public static string[] ToPathList(string content) =>
        content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
