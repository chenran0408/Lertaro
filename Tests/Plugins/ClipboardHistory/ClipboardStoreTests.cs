using System.Text;

namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardStoreTests
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "LertaroClipboardTests");

    private string _databasePath = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        Directory.CreateDirectory(TempRoot);
        _databasePath = Path.Combine(TempRoot, $"clipboard-store-{Guid.NewGuid():N}.db");
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Corruption recovery renames files aside with a suffix on the same stem, so remove by prefix.
        var stem = Path.GetFileNameWithoutExtension(_databasePath);
        foreach (var file in Directory.GetFiles(TempRoot, stem + "*"))
        {
            File.Delete(file);
        }
    }

    private ClipboardStore CreateStore() => new(_databasePath);

    [TestMethod]
    public async Task PreviewFlattensUnicodeLineSeparatorsWithoutChangingStoredText()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        const string text = "first\u2028second\u2029third\u0085last";
        var entry = await store.AddAsync(text, "editor", "Document", 500, 1000000);
        Assert.IsNotNull(entry);
        Assert.AreEqual(text, entry.Content);
        Assert.AreEqual("first second third last", entry.PlainPreview);
    }

    [TestMethod]
    public async Task InitializeAsync_Twice_Succeeds()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        await store.InitializeAsync();
    }

    [TestMethod]
    public async Task AddAsync_StoresAllFields()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var entry = await store.AddAsync("hello", "Notepad", "Untitled - Notepad", maxEntries: 500, maxTextLength: 1000000);

        Assert.IsNotNull(entry);
        var entries = await store.GetEntriesAsync();
        Assert.HasCount(1, entries);
        Assert.AreEqual(entry.Id, entries[0].Id);
        Assert.AreEqual("hello", entries[0].Content);
        Assert.AreEqual("hello", entries[0].PlainPreview);
        Assert.AreEqual("Notepad", entries[0].SourceProcess);
        Assert.AreEqual("Untitled - Notepad", entries[0].SourceWindowTitle);
        Assert.IsFalse(entries[0].Pinned);
        Assert.AreEqual(1L, entries[0].SeenCount);
        Assert.AreEqual(entries[0].CreatedAtMs, entries[0].LastSeenAtMs);
    }

    [TestMethod]
    public async Task AddAsync_DuplicateText_MergesIntoExistingRow()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var first = await store.AddAsync("same text", "AppA", "TitleA", 500, 1000000);
        var second = await store.AddAsync("same text", "AppB", "TitleB", 500, 1000000);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(2L, second.SeenCount);
        Assert.AreEqual("AppB", second.SourceProcess);
        var entries = await store.GetEntriesAsync();
        Assert.HasCount(1, entries);
    }

    [TestMethod]
    public async Task AddAsync_DuplicatePromotion_IsStrictlyNewest()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var first = await store.AddAsync("alpha", "App", "Title", 500, 1000000);
        var second = await store.AddAsync("beta", "App", "Title", 500, 1000000);
        var promoted = await store.AddAsync("alpha", "App", "Title", 500, 1000000);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsNotNull(promoted);
        Assert.AreEqual(first.Id, promoted.Id);
        var entries = await store.GetEntriesAsync();
        Assert.HasCount(2, entries);
        Assert.AreEqual(promoted.Id, entries[0].Id);
        Assert.IsGreaterThan(second.LastSeenAtMs, entries[0].LastSeenAtMs);
    }

    [TestMethod]
    public async Task AddAsync_TextTruncatedAtSurrogateBoundary()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var text = new string('a', 999) + "😀😀😀";

        var entry = await store.AddAsync(text, "App", "Title", 500, maxTextLength: 1000);

        Assert.IsNotNull(entry);
        Assert.IsLessThanOrEqualTo(1000, entry.Content.Length);
        Assert.IsFalse(char.IsHighSurrogate(entry.Content[^1]));
        // The expected cut is the first 1000 chars minus a trailing high surrogate half.
        var expectedLength = char.IsHighSurrogate(text[999]) ? 999 : 1000;
        Assert.AreEqual(expectedLength, entry.Content.Length);
    }

    [TestMethod]
    public async Task AddAsync_PreviewIsSingleLineAndCapped()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var text = new StringBuilder()
            .Append(new string('x', 300)).Append("\r\n").Append(new string('y', 300)).Append('\t').Append("end")
            .ToString();

        var entry = await store.AddAsync(text, "App", "Title", 500, 1000000);

        Assert.IsNotNull(entry);
        Assert.HasCount(200, entry.PlainPreview.ToCharArray());
        Assert.IsFalse(entry.PlainPreview.Contains('\r'));
        Assert.IsFalse(entry.PlainPreview.Contains('\n'));
        Assert.IsFalse(entry.PlainPreview.Contains('\t'));
        Assert.AreEqual(new string('x', 200), entry.PlainPreview);
    }

    [TestMethod]
    public async Task AddAsync_KeepsOnlyNewestUnpinnedWithinCap()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        for (var i = 0; i < 5; i++)
            await store.AddAsync($"text {i}", "App", "Title", maxEntries: 3, 1000000);

        var entries = await store.GetEntriesAsync();
        Assert.HasCount(3, entries);
        // Newest first, so the last three inserted texts survive the cap.
        Assert.AreEqual("text 4", entries[0].Content);
        Assert.AreEqual("text 3", entries[1].Content);
        Assert.AreEqual("text 2", entries[2].Content);
    }

    [TestMethod]
    public async Task TrimAsync_PinnedEntriesExemptFromCap()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        for (var i = 0; i < 5; i++)
            await store.AddAsync($"text {i}", "App", "Title", 500, 1000000);
        var entries = await store.GetEntriesAsync();
        var oldest = entries.Single(entry => entry.Content == "text 0");

        await store.SetPinnedAsync(oldest.Id, pinned: true);
        await store.TrimAsync(maxEntries: 3);

        var trimmed = await store.GetEntriesAsync();
        // The pinned row survives on top of the three newest unpinned rows.
        Assert.HasCount(4, trimmed);
        var pinned = trimmed.Single(entry => entry.Id == oldest.Id);
        Assert.IsTrue(pinned.Pinned);
        Assert.IsFalse(trimmed.Any(entry => entry.Content == "text 1"));
    }

    [TestMethod]
    public async Task SetPinnedAsync_TogglesFlag()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var entry = await store.AddAsync("pin me", "App", "Title", 500, 1000000);

        await store.SetPinnedAsync(entry!.Id, pinned: true);
        var pinned = (await store.GetEntriesAsync()).Single(item => item.Id == entry.Id);
        Assert.IsTrue(pinned.Pinned);

        await store.SetPinnedAsync(entry.Id, pinned: false);
        var unpinned = (await store.GetEntriesAsync()).Single(item => item.Id == entry.Id);
        Assert.IsFalse(unpinned.Pinned);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesRow()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var kept = await store.AddAsync("kept", "App", "Title", 500, 1000000);
        var removed = await store.AddAsync("removed", "App", "Title", 500, 1000000);

        await store.DeleteAsync(removed!.Id);

        var entries = await store.GetEntriesAsync();
        Assert.HasCount(1, entries);
        Assert.AreEqual(kept!.Id, entries[0].Id);
    }

    [TestMethod]
    public async Task SearchAsync_UsesSqliteFullTextAndKeepsPinnedNewestOrder()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var older = await store.AddAsync("alpha older", "App", "Title", 500, 1000000);
        var newer = await store.AddAsync("alpha newer", "App", "Title", 500, 1000000);
        var pinned = await store.AddAsync("alpha pinned", "App", "Title", 500, 1000000);
        await store.SetPinnedAsync(pinned!.Id, true);

        var results = await store.SearchAsync("alpha");

        CollectionAssert.AreEqual(new[] { pinned.Id, newer!.Id, older!.Id }, results.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public async Task SearchAsync_SupportsShortTermsAndFtsSpecialCharacters()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var shortTerm = await store.AddAsync("C# / C++ _100%", "App", "Title", 500, 1000000);
        var other = await store.AddAsync("unrelated", "App", "Title", 500, 1000000);

        var shortResults = await store.SearchAsync("C#");
        var specialResults = await store.SearchAsync("_100%");

        Assert.AreEqual(shortTerm!.Id, shortResults.Single().Id);
        Assert.AreEqual(shortTerm.Id, specialResults.Single().Id);
        Assert.IsFalse(specialResults.Any(item => item.Id == other!.Id));
    }

    [TestMethod]
    public async Task SearchAsync_RemovesDeletedRowsAndSurvivesReopen()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var removed = await store.AddAsync("remove-me", "App", "Title", 500, 1000000);
        await store.AddAsync("keep-me", "App", "Title", 500, 1000000);
        await store.DeleteAsync(removed!.Id);
        Assert.IsEmpty(await store.SearchAsync("remove-me"));

        var reopened = CreateStore();
        await reopened.InitializeAsync();
        var results = await reopened.SearchAsync("keep-me");
        Assert.HasCount(1, results);
        Assert.AreEqual("keep-me", results[0].Content);
    }

    [TestMethod]
    public async Task Entries_PersistAcrossStoreReopen()
    {
        var firstStore = CreateStore();
        await firstStore.InitializeAsync();
        var expected = await firstStore.AddAsync("persisted text", "App", "Title", 500, 1000000);

        var reopened = CreateStore();
        await reopened.InitializeAsync();
        var entries = await reopened.GetEntriesAsync();

        Assert.HasCount(1, entries);
        Assert.AreEqual(expected!.Id, entries[0].Id);
        Assert.AreEqual("persisted text", entries[0].Content);
    }

    [TestMethod]
    public async Task InitializeAsync_CorruptDatabaseWithSidecars_IsRenamedAsideAndRebuilt()
    {
        await File.WriteAllTextAsync(_databasePath, "this is definitely not a sqlite database file, just filler bytes");
        await File.WriteAllTextAsync(_databasePath + "-journal", "stale journal sidecar");

        var store = CreateStore();
        await store.InitializeAsync();

        Assert.IsEmpty(await store.GetEntriesAsync());
        Assert.IsFalse(File.Exists(_databasePath + "-journal"));
        // SQLite may discard an invalid cold journal before it reports NOTADB.
        // The database itself is always quarantined; remaining sidecars are tested separately.
        Assert.HasCount(1, Directory.GetFiles(TempRoot, Path.GetFileName(_databasePath) + ".corrupt-*"));

        // The rebuilt database is usable: an add lands and reads back.
        var entry = await store.AddAsync("after recovery", "App", "Title", 500, 1000000);
        Assert.IsNotNull(entry);
        Assert.HasCount(1, await store.GetEntriesAsync());
    }

    [TestMethod]
    public async Task GetEntriesAsync_CancelledToken_ThrowsWithoutHiddenCancellation()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.GetEntriesAsync(cancellation.Token));
    }
}
