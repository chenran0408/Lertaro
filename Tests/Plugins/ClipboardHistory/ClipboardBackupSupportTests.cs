using System.IO;
using System.IO.Compression;

namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardBackupSupportTests
{
    // MethodLevel parallelization: every test gets its own paths and cleans only those, exactly
    // like ClipboardStoreTests -- one shared TempRoot, never a shared file.
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "LertaroClipboardBackupTests");

    private string _databasePath = string.Empty;
    private string _imagesDirectory = string.Empty;
    private string _zipPath = string.Empty;
    private string _restoreRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(TempRoot);
        _databasePath = Path.Combine(TempRoot, $"clipboard-{id}.db");
        _imagesDirectory = Path.Combine(TempRoot, $"images-{id}");
        _zipPath = Path.Combine(TempRoot, $"backup-{id}.zip");
        _restoreRoot = Path.Combine(TempRoot, $"restore-{id}");
        Directory.CreateDirectory(_imagesDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var stem = Path.GetFileNameWithoutExtension(_databasePath);
        foreach (var file in Directory.GetFiles(TempRoot, stem + "*"))
            File.Delete(file);
        if (Directory.Exists(_imagesDirectory)) Directory.Delete(_imagesDirectory, recursive: true);
        if (Directory.Exists(_restoreRoot)) Directory.Delete(_restoreRoot, recursive: true);
        if (File.Exists(_zipPath)) File.Delete(_zipPath);
    }

    private ClipboardStore CreateStore() => new(_databasePath, _imagesDirectory);

    [TestMethod]
    public async Task Export_ThenExtract_RestoresEntriesNotesAndImages()
    {
        // Source history: two texts, one noted and pinned, one image with real bytes on disk.
        var store = CreateStore();
        await store.InitializeAsync();
        var noted = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Text, "noted entry"),
            "App", "Title", 500, 1000000);
        Assert.IsNotNull(noted);
        await store.SetNoteAsync(noted.Id, "backup me");
        await store.SetPinnedAsync(noted.Id, true);
        var image = await store.AddAsync(new ClipboardEntryDraft(ClipboardEntryKind.Image, "")
        {
            ImagePath = "shot.png",
            ImageWidth = 10,
            ImageHeight = 10,
        }, "App", "Title", 500, 1000000);
        Assert.IsNotNull(image);
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        await File.WriteAllBytesAsync(Path.Combine(_imagesDirectory, "shot.png"), pngBytes);

        await store.ExportAsync(_zipPath);
        Assert.IsTrue(File.Exists(_zipPath));

        // Restore into fresh locations, as an import on a new machine would.
        Directory.CreateDirectory(_restoreRoot);
        var (restoredDatabase, restoredImages) = ClipboardBackupSupport.Extract(_zipPath, _restoreRoot);

        var restored = new ClipboardStore(restoredDatabase, restoredImages);
        await restored.InitializeAsync();
        var entries = await restored.GetEntriesAsync();

        Assert.HasCount(2, entries);
        var restoredNote = entries.Single(entry => entry.Content == "noted entry");
        Assert.AreEqual("backup me", restoredNote.Note);
        Assert.IsTrue(restoredNote.Pinned);
        var restoredImage = entries.Single(entry => entry.Kind == ClipboardEntryKind.Image);
        Assert.AreEqual("shot.png", restoredImage.ImagePath);
        Assert.IsTrue(File.Exists(Path.Combine(restoredImages, "shot.png")));
    }

    [TestMethod]
    public void Extract_RejectsAZipWithoutADatabase()
    {
        using (var archive = ZipFile.Open(_zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not a backup");
        }

        Assert.ThrowsExactly<InvalidDataException>(() => ClipboardBackupSupport.Extract(_zipPath, _restoreRoot));
        Assert.IsFalse(Directory.Exists(_restoreRoot)); // the rejected staging does not linger
    }
}
