using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.BrowserData.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BrowserDataCacheTests
{
    [TestInitialize]
    public void ResetBefore() => PluginSettingsService.IsComponentEnabledFunc = null;

    [TestCleanup]
    public void ResetAfter() => PluginSettingsService.IsComponentEnabledFunc = null;

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static void WriteBookmarksFile(string profileDir) => File.WriteAllText(Path.Combine(profileDir, "Bookmarks"), """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "name": "Example", "url": "https://example.com" }
        ] } } }
        """);

    private static void WriteHistoryDb(string profileDir)
    {
        using var conn = new SqliteConnection($"Data Source={Path.Combine(profileDir, "History")};Pooling=False");
        conn.Open();
        using var create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE urls (id INTEGER PRIMARY KEY, url TEXT, title TEXT, last_visit_time INTEGER, hidden INTEGER)";
        create.ExecuteNonQuery();
        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO urls (url, title, last_visit_time, hidden) VALUES ('https://visited.com', 'Visited', 100, 0)";
        insert.ExecuteNonQuery();
    }

    private static void WritePlacesDb(string profileDir)
    {
        using var conn = new SqliteConnection($"Data Source={Path.Combine(profileDir, "places.sqlite")};Pooling=False");
        conn.Open();
        using var create = conn.CreateCommand();
        create.CommandText = """
            CREATE TABLE moz_places (id INTEGER PRIMARY KEY, url TEXT, title TEXT, last_visit_date INTEGER, hidden INTEGER);
            CREATE TABLE moz_bookmarks (id INTEGER PRIMARY KEY, type INTEGER, fk INTEGER, title TEXT);
            INSERT INTO moz_places (id, url, title, last_visit_date, hidden) VALUES (1, 'https://visited.com', 'Visited', 100, 0);
            INSERT INTO moz_bookmarks (id, type, fk, title) VALUES (1, 1, 1, 'Example');
            """;
        create.ExecuteNonQuery();
    }

    private static string MakeSubDirectory(string parentDir, string name) =>
        Directory.CreateDirectory(Path.Combine(parentDir, name)).FullName;

    private static string MakeSubChromiumProfile(string parentDir, string name)
    {
        var dir = MakeSubDirectory(parentDir, name);
        WriteBookmarksFile(dir);
        WriteHistoryDb(dir);
        return dir;
    }

    private static string MakeSubFirefoxProfile(string parentDir, string name)
    {
        var dir = MakeSubDirectory(parentDir, name);
        WritePlacesDb(dir);
        return dir;
    }

    private static List<BrowserProfileConfig> ProfileConfig(string path) =>
        new() { new BrowserProfileConfig { Name = "Test", Path = path } };

    [TestMethod]
    public void GetSnapshot_DisabledComponent_DoesNotLoadSnapshot()
    {
        PluginSettingsService.IsComponentEnabledFunc = (_, _, _) => false;

        Assert.IsEmpty(BrowserDataCache.GetSnapshot());
    }

    [TestMethod]
    public void LoadAll_BothEnabled_ReturnsBookmarksAndHistory()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: true);

        var entries = result.Single();
        Assert.HasCount(1, entries.Bookmarks);
        Assert.HasCount(1, entries.History);
    }

    [TestMethod]
    public void AttachFavicons_SharesBitmapForIdenticalBytesButNotDifferentBytes()
    {
        var firstBytes = CreatePng(0x10);
        var sameBytes = firstBytes.ToArray();
        var differentBytes = CreatePng(0x20);
        var entries = new ProfileEntries
        {
            Profile = new BrowserProfileConfig { Name = "Test", Path = "unused" },
            Bookmarks =
            {
                new BrowserEntry("First", "https://first.example", true, 0),
                new BrowserEntry("Second", "https://second.example", true, 0),
                new BrowserEntry("Third", "https://third.example", true, 0),
            }
        };
        var decoded = new Dictionary<byte[], BitmapSource?>(BrowserDataCache.ByteArrayContentComparer.Instance);

        BrowserDataCache.AttachFavicons(entries, new Dictionary<string, byte[]>
        {
            ["https://first.example"] = firstBytes,
            ["https://second.example"] = sameBytes,
            ["https://third.example"] = differentBytes,
        }, decoded);

        Assert.IsNotNull(entries.Bookmarks[0].Favicon);
        Assert.AreSame(entries.Bookmarks[0].Favicon, entries.Bookmarks[1].Favicon);
        Assert.IsNotNull(entries.Bookmarks[2].Favicon);
        Assert.AreNotSame(entries.Bookmarks[0].Favicon, entries.Bookmarks[2].Favicon);
    }

    [TestMethod]
    public void AttachFavicons_InvalidPayload_RemainsNullAndDoesNotPreventOtherIcons()
    {
        var entries = new ProfileEntries
        {
            Profile = new BrowserProfileConfig { Name = "Test", Path = "unused" },
            Bookmarks =
            {
                new BrowserEntry("Invalid", "https://invalid.example", true, 0),
                new BrowserEntry("Valid", "https://valid.example", true, 0),
            }
        };
        var decoded = new Dictionary<byte[], BitmapSource?>(BrowserDataCache.ByteArrayContentComparer.Instance);

        BrowserDataCache.AttachFavicons(entries, new Dictionary<string, byte[]>
        {
            ["https://invalid.example"] = [0x01, 0x02, 0x03],
            ["https://valid.example"] = CreatePng(0x20),
        }, decoded);

        Assert.IsNull(entries.Bookmarks[0].Favicon);
        Assert.IsNotNull(entries.Bookmarks[1].Favicon);
    }

    private static byte[] CreatePng(byte blue)
    {
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { blue, 0, 0, 0xFF }, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [TestMethod]
    public void LoadAll_BlacklistFiltersBookmarkAndHistoryEntries()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(
            ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: true, blacklist: ["example.com"]);

        var entries = result.Single();
        Assert.IsEmpty(entries.Bookmarks);
        Assert.HasCount(1, entries.History);
    }

    [TestMethod]
    public void LoadAll_BookmarksDisabled_SkipsBookmarksButKeepsHistory()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: false, indexHistory: true);

        var entries = result.Single();
        Assert.IsEmpty(entries.Bookmarks);
        Assert.HasCount(1, entries.History);
    }

    [TestMethod]
    public void LoadAll_HistoryDisabled_SkipsHistoryButKeepsBookmarks()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: false);

        var entries = result.Single();
        Assert.HasCount(1, entries.Bookmarks);
        Assert.IsEmpty(entries.History);
    }

    [TestMethod]
    public void LoadAll_BothDisabled_ReturnsNoProfiles()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: false, indexHistory: false);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void HaveProfileFilesChanged_UnchangedFiles_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        var bookmarkPath = Path.Combine(dir.Path, "Bookmarks");
        File.SetLastWriteTimeUtc(bookmarkPath, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var changed = BrowserDataCache.HaveProfileFilesChanged(
            ProfileConfig(dir.Path), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void HaveProfileFilesChanged_ModifiedFile_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        var bookmarkPath = Path.Combine(dir.Path, "Bookmarks");
        File.SetLastWriteTimeUtc(bookmarkPath, new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        var changed = BrowserDataCache.HaveProfileFilesChanged(
            ProfileConfig(dir.Path), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsTrue(changed);
    }

    [TestMethod]
    public void LoadAll_ConfiguredFolderOfProfiles_LoadsEveryProfileItHolds()
    {
        using var dir = new TempDirectory();
        MakeSubChromiumProfile(dir.Path, "Default");
        MakeSubChromiumProfile(dir.Path, "Profile 1");
        MakeSubDirectory(dir.Path, "Crashpad");

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: true);

        Assert.HasCount(2, result);
        CollectionAssert.AreEqual(
            new[] { Path.Combine(dir.Path, "Default"), Path.Combine(dir.Path, "Profile 1") },
            result.Select(entries => entries.Profile.Path).ToList());
        Assert.HasCount(1, result[0].Bookmarks);
        Assert.HasCount(1, result[1].Bookmarks);
    }

    [TestMethod]
    public void LoadAll_FirefoxProfilesFolder_LoadsEveryProfileAndNamesItReadablely()
    {
        // A Firefox install names each profile folder after a random token, which is exactly why the
        // shipped default points at the folder holding them rather than at one profile.
        using var dir = new TempDirectory();
        MakeSubFirefoxProfile(dir.Path, "abc12345.default-release");
        MakeSubFirefoxProfile(dir.Path, "zyx98765.dev-edition-default");
        var stub = MakeSubDirectory(dir.Path, "k7cm2jgl.profiles");
        File.WriteAllText(Path.Combine(stub, "times.json"), "{}");

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: false);

        Assert.HasCount(2, result);
        Assert.AreEqual("Test · default-release", result[0].Profile.Name);
        Assert.AreEqual("Test · dev-edition-default", result[1].Profile.Name);
        Assert.HasCount(1, result[0].Bookmarks);
        Assert.IsEmpty(result[0].History);
    }

    [TestMethod]
    public void LoadAll_SingleProfileUnderAConfiguredFolder_KeepsTheConfiguredName()
    {
        using var dir = new TempDirectory();
        MakeSubChromiumProfile(dir.Path, "Default");

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: false);

        Assert.AreEqual("Test", result.Single().Profile.Name);
    }

    [TestMethod]
    public void LoadAll_ConfiguredFolderKeepsItsOwnIconForEveryProfileInside()
    {
        using var dir = new TempDirectory();
        MakeSubChromiumProfile(dir.Path, "Default");
        MakeSubChromiumProfile(dir.Path, "Profile 1");

        var result = BrowserDataCache.LoadAll(
            new List<BrowserProfileConfig> { new() { Name = "Test", Icon = "M0 0h1v1z", Path = dir.Path } },
            indexBookmarks: true, indexHistory: false);

        Assert.AreEqual("M0 0h1v1z", result[0].Profile.Icon);
        Assert.AreEqual("M0 0h1v1z", result[1].Profile.Icon);
    }

    [TestMethod]
    public void LoadAll_FolderWithNoProfilesInIt_ReturnsNoProfiles()
    {
        using var dir = new TempDirectory();
        MakeSubDirectory(dir.Path, "sub");

        Assert.IsEmpty(BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: true));
    }

    [TestMethod]
    public void HaveProfileFilesChanged_NewerFileInProfileUnderTheConfiguredFolder_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        var bookmarkPath = WriteDatedBookmarkInSubProfile(dir.Path, "Default", new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        var changed = BrowserDataCache.HaveProfileFilesChanged(
            ProfileConfig(dir.Path), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsTrue(changed);
    }

    [TestMethod]
    public void HaveProfileFilesChanged_NothingNewerUnderTheConfiguredFolder_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        WriteDatedBookmarkInSubProfile(dir.Path, "Default", new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        var changed = BrowserDataCache.HaveProfileFilesChanged(
            ProfileConfig(dir.Path), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsFalse(changed);
    }

    [TestMethod]
    public void HaveIndexedFilesChanged_NewerWatchedFile_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        WriteDatedFile(dir.Path, "Bookmarks", new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsTrue(BrowserDataCache.HaveIndexedFilesChanged(
            [dir.Path], new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
    }

    [TestMethod]
    public void HaveIndexedFilesChanged_NothingNewer_ReturnsFalse()
    {
        using var dir = new TempDirectory();
        WriteDatedFile(dir.Path, "Bookmarks", new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsFalse(BrowserDataCache.HaveIndexedFilesChanged(
            [dir.Path], new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
    }

    [TestMethod]
    public void HaveIndexedFilesChanged_NewerFileThatIsNotIndexedData_ReturnsFalse()
    {
        // A running browser rewrites plenty under a profile that this plugin never reads -- and it does so
        // on its own schedule, not the user's. Taking those into account would turn every query into a
        // reload, which is the cost this probe is supposed to avoid.
        using var dir = new TempDirectory();
        WriteDatedFile(dir.Path, "preferences", new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        WriteDatedFile(dir.Path, "Bookmarks", new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsFalse(BrowserDataCache.HaveIndexedFilesChanged(
            [dir.Path], new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
    }

    [TestMethod]
    public void HaveIndexedFilesChanged_NewerBakCopy_ReturnsTrue()
    {
        // A profile whose bookmarks currently live only in Bookmarks.bak (the reader's fallback) still has
        // to answer a bookmark added seconds ago, rather than waiting for the coarse refresh.
        using var dir = new TempDirectory();
        WriteDatedFile(dir.Path, "Bookmarks.bak", new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsTrue(BrowserDataCache.HaveIndexedFilesChanged(
            [dir.Path], new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
    }

    [TestMethod]
    public void HaveIndexedFilesChanged_NothingIndexedYet_ReturnsFalse() =>
        // The coarse re-walk owns discovering a profile for the first time; per query this must say "no"
        // rather than reloading against an empty snapshot forever.
        Assert.IsFalse(BrowserDataCache.HaveIndexedFilesChanged([], DateTime.UtcNow));

    [TestMethod]
    public void HaveIndexedFilesChanged_NeverLoaded_ReturnsTrue() =>
        Assert.IsTrue(BrowserDataCache.HaveIndexedFilesChanged([], DateTime.MinValue));

    [TestMethod]
    public void HaveIndexedFilesChanged_ProfileFolderDeletedAfterTheLoad_ReturnsFalse()
    {
        // Signing out of a browser can remove the folder under a live snapshot. The probe runs per query on
        // the UI thread, so it reports "nothing moved" rather than throwing -- the next configured re-walk
        // drops the profile for good.
        using var dir = new TempDirectory();
        var gone = MakeSubDirectory(dir.Path, "Deleted");

        Assert.IsFalse(BrowserDataCache.HaveIndexedFilesChanged(
            [gone], new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc)));
    }

    private static void WriteDatedFile(string dir, string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "{}");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    // A sub-profile holding just one dated Bookmarks file: writing the others too would leave them
    // stamped "now", and the probe under test returns true on any single newer monitored file.
    private static string WriteDatedBookmarkInSubProfile(string parentDir, string profileName, DateTime lastWriteUtc)
    {
        var profileDir = MakeSubDirectory(parentDir, profileName);
        var bookmarkPath = Path.Combine(profileDir, "Bookmarks");
        WriteBookmarksFile(profileDir);
        File.SetLastWriteTimeUtc(bookmarkPath, lastWriteUtc);
        return bookmarkPath;
    }
}
