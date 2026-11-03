using Microsoft.Data.Sqlite;
using System.Text;

namespace Lertaro.Plugins.ClipboardHistory.Tests;

/// <summary>Tests for the multi-kind store: file-path and image drafts, content-addressed image
/// files, the retention window and the pre-kind schema migration.</summary>
[TestClass]
public sealed class ClipboardStoreKindTests
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "LertaroClipboardKindTests");

    private string _databasePath = string.Empty;
    private string _imagesDirectory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TempRoot);
        _databasePath = Path.Combine(TempRoot, $"clipboard-kind-{Guid.NewGuid():N}.db");
        _imagesDirectory = Path.Combine(TempRoot, $"clipboard-kind-images-{Guid.NewGuid():N}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        var stem = Path.GetFileNameWithoutExtension(_databasePath);
        foreach (var file in Directory.GetFiles(TempRoot, stem + "*"))
            File.Delete(file);
        if (Directory.Exists(_imagesDirectory)) Directory.Delete(_imagesDirectory, recursive: true);
    }

    private ClipboardStore CreateStore() => new(_databasePath, _imagesDirectory);

    private static byte[] PngBytes(string marker) => Encoding.UTF8.GetBytes("png:" + marker);

    private string WriteImageFile(string hashHex, byte[] png)
    {
        Directory.CreateDirectory(_imagesDirectory);
        var path = Path.Combine(_imagesDirectory, hashHex + ".png");
        File.WriteAllBytes(path, png);
        return path;
    }

    [TestMethod]
    public async Task AddAsync_FileList_DedupsCaseAndOrderInsensitively()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var first = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.FilePaths, @"C:\A.txt" + "\n" + @"C:\b.txt"),
            "Explorer", "Downloads", 500, 1000000);
        var second = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.FilePaths, @"c:\a.TXT" + "\n" + @"C:\B.txt"),
            "Explorer", "Downloads", 500, 1000000);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.Id, second.Id);
        Assert.HasCount(1, await store.GetEntriesAsync());
        // The stored content is the normalized (uppercased) joined paths, so FTS can search it.
        Assert.AreEqual(@"C:\A.TXT" + "\n" + @"C:\B.TXT", second.Content.ToUpperInvariant());
    }

    [TestMethod]
    public async Task AddAsync_Image_StoresKindAndDimensionsAndDedupsByHash()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var png = PngBytes("one");

        var first = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Image, string.Empty,
            "aaaa.png", 1920, 1080, "aaaa"), "SnippingTool", "Screenshot", 500, 1000000);
        var second = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Image, string.Empty,
            "aaaa.png", 1920, 1080, "aaaa"), "SnippingTool", "Screenshot", 500, 1000000);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(ClipboardEntryKind.Image, second.Kind);
        Assert.AreEqual(1920, second.ImageWidth);
        Assert.AreEqual(1080, second.ImageHeight);
        Assert.HasCount(1, await store.GetEntriesAsync());
    }

    [TestMethod]
    public async Task AddAsync_DifferentKinds_NeverMergeIntoOneRow()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var png = PngBytes("shared");

        // Same marker bytes for the text and the file name: identity is scoped by kind.
        var text = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Text, "shared"),
            "App", "Title", 500, 1000000);
        var image = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Image, string.Empty,
            "bbbb.png", 10, 10, ClipboardStoreSupport.HashToHex(System.Security.Cryptography.SHA256.HashData(png))),
            "App", "Title", 500, 1000000);

        Assert.IsNotNull(text);
        Assert.IsNotNull(image);
        Assert.AreNotEqual(text.Id, image.Id);
        Assert.HasCount(2, await store.GetEntriesAsync());
    }

    [TestMethod]
    public async Task DeleteAsync_ImageRow_UnlinksContentAddressedFile()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var imagePath = WriteImageFile("cccc", PngBytes("doomed"));
        var entry = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Image, string.Empty,
            "cccc.png", 10, 10, "cccc"), "App", "Title", 500, 1000000);

        await store.DeleteAsync(entry!.Id);

        Assert.IsEmpty(await store.GetEntriesAsync());
        Assert.IsFalse(File.Exists(imagePath));
    }

    [TestMethod]
    public async Task ClearAsync_ImageRows_UnlinkEveryFile()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var imageA = WriteImageFile("dddd", PngBytes("a"));
        var imageB = WriteImageFile("eeee", PngBytes("b"));
        await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Image, string.Empty,
            "dddd.png", 10, 10, "dddd"), "App", "Title", 500, 1000000);
        await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Image, string.Empty,
            "eeee.png", 10, 10, "eeee"), "App", "Title", 500, 1000000);

        await store.ClearAsync();

        Assert.IsEmpty(await store.GetEntriesAsync());
        Assert.IsFalse(File.Exists(imageA));
        Assert.IsFalse(File.Exists(imageB));
    }

    [TestMethod]
    public async Task TrimAsync_RetentionWindow_RemovesStaleUnpinnedOnly()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var stale = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Text, "stale"),
            "App", "Title", 500, 1000000);
        var fresh = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Text, "fresh"),
            "App", "Title", 500, 1000000);

        // Age the stale row beyond a 1-day window without touching the fresh one.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = _databasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE clipboard_history SET last_seen_at_ms = last_seen_at_ms - 2 * 86400000 WHERE id = @id";
            command.Parameters.AddWithValue("@id", stale!.Id);
            command.ExecuteNonQuery();
        }

        await store.TrimAsync(maxEntries: 500, retentionDays: 1);

        var entries = await store.GetEntriesAsync();
        Assert.HasCount(1, entries);
        Assert.AreEqual(fresh!.Id, entries[0].Id);
    }

    [TestMethod]
    public async Task TrimAsync_RetentionWindow_PinnedEntriesExempt()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var stale = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Text, "stale pinned"),
            "App", "Title", 500, 1000000);
        await store.SetPinnedAsync(stale!.Id, true);

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = _databasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE clipboard_history SET last_seen_at_ms = last_seen_at_ms - 400 * 86400000 WHERE id = @id";
            command.Parameters.AddWithValue("@id", stale.Id);
            command.ExecuteNonQuery();
        }

        await store.TrimAsync(maxEntries: 500, retentionDays: 7);

        Assert.HasCount(1, await store.GetEntriesAsync());
    }

    [TestMethod]
    public async Task InitializeAsync_PreKindDatabase_MigratesInPlaceAndKeepsRows()
    {
        // A database exactly as 6.3.x created it: text-only schema, one row, no kind columns.
        Directory.CreateDirectory(_imagesDirectory);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = _databasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE clipboard_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    content TEXT NOT NULL,
                    plain_preview TEXT NOT NULL,
                    content_sha256 BLOB NOT NULL,
                    source_process TEXT NOT NULL DEFAULT '',
                    source_window_title TEXT NOT NULL DEFAULT '',
                    pinned INTEGER NOT NULL DEFAULT 0,
                    seen_count INTEGER NOT NULL DEFAULT 1,
                    created_at_ms INTEGER NOT NULL,
                    last_seen_at_ms INTEGER NOT NULL
                );
                INSERT INTO clipboard_history
                    (content, plain_preview, content_sha256, created_at_ms, last_seen_at_ms)
                VALUES ('legacy text', 'legacy text', x'00', 100, 100);
                """;
            command.ExecuteNonQuery();
        }

        var store = CreateStore();
        await store.InitializeAsync();

        // The legacy row is readable as a Text entry and new columns exist for new captures.
        var entries = await store.GetEntriesAsync();
        Assert.HasCount(1, entries);
        Assert.AreEqual(ClipboardEntryKind.Text, entries[0].Kind);
        Assert.AreEqual("legacy text", entries[0].Content);
        var added = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Text, "new text"),
            "App", "Title", 500, 1000000);
        Assert.IsNotNull(added);
    }
}
