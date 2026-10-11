using System.Runtime.CompilerServices;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

using Lertaro.Plugins.CoreExtensions.Providers.QuickPanel;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.QuickPanel;

// Whether the folder the user was last browsing is worth offering as a tab. The listing itself goes
// through the host's index and cannot be reached from a test; this is the decision in front of it.
[TestClass]
[DoNotParallelize]
public sealed class LastDirectoryTabProviderTests
{
    private Func<string?>? _oldPath;
    private Func<string, bool, string, int, CancellationToken, IAsyncEnumerable<ISearchResult>>? _oldEnumeration;

    [TestInitialize]
    public void SaveHostDelegates()
    {
        _oldPath = ExplorerPathService.GetLastActivePathFunc;
        _oldEnumeration = DirectoryIndexerService.EnumerateDirectoryFunc;
    }

    [TestCleanup]
    public void RestoreHostDelegates()
    {
        ExplorerPathService.GetLastActivePathFunc = _oldPath;
        DirectoryIndexerService.EnumerateDirectoryFunc = _oldEnumeration;
    }

    [TestMethod]
    [DataRow(150)]
    [DataRow(2000)]
    public async Task ListingReturnsEveryIndexedEntryWithoutAHundredItemCap(int count)
    {
        using var dir = new TempDirectory();
        ExplorerPathService.GetLastActivePathFunc = () => dir.Path;
        var requestedLimit = -1;
        DirectoryIndexerService.EnumerateDirectoryFunc = (_, _, _, limit, token) =>
        {
            requestedLimit = limit;
            return Enumerate(limit > 0 ? Math.Min(limit, count) : count, token);
        };
        var entries = await new LastDirectoryTabProvider().GetEntriesAsync();
        Assert.AreEqual(0, requestedLimit);
        Assert.HasCount(count, entries);
    }

    private static async IAsyncEnumerable<ISearchResult> Enumerate(int count, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.CompletedTask;
        for (var index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            yield return new Entry(index.ToString());
        }
    }

    private sealed record Entry(string Name) : ISearchResult
    {
        public string FullPath => @"C:\data\" + Name;
        public string ContextDirectory => @"C:\data";
        public bool IsDir => false;
        public bool IsApplication => false;
    }

    [TestMethod]
    public void ARealFolderIsWorthShowing()
    {
        using var dir = new TempDirectory();

        Assert.IsTrue(LastDirectoryTabProvider.IsWorthShowing(dir.Path));
    }

    [TestMethod]
    public void NothingBrowsedYet_IsNot()
    {
        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(null));
        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(string.Empty));
    }

    // The tracker records where the user went; the folder can have been deleted or unplugged since.
    [TestMethod]
    public void AFolderThatIsGone_IsNot()
        => Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(
            Path.Combine(Path.GetTempPath(), "lertaro-no-such-folder")));

    // Explorer and every file dialog land on the Desktop before anything more specific is browsed to,
    // so treating it as "last visited" would leave this tab permanently showing a place nobody chose.
    [TestMethod]
    public void TheDesktopIsNot_HoweverItIsSpelled()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(desktop));
        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(desktop + "\\"));
        Assert.IsFalse(LastDirectoryTabProvider.IsWorthShowing(desktop.ToUpperInvariant()));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
