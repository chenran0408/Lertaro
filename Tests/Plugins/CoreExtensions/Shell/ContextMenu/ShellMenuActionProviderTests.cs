using Lertaro.PluginSdk.Abstractions;
using Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;

namespace Lertaro.Plugins.CoreExtensions.Tests.Shell.ContextMenu;

[TestClass]
public sealed class ShellMenuActionProviderTests
{
    private sealed class FakeResult : ISearchResult
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string ContextDirectory { get; init; } = "";
        public bool IsDir { get; init; }
        public bool IsApplication { get; init; }
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lertaro-tests-{Guid.NewGuid():N}.txt");

        public TempFile() => File.WriteAllText(Path, "x");

        public void Dispose()
        {
            try { File.Delete(Path); } catch { }
        }
    }

    [TestMethod]
    [DataRow(".exe")]
    [DataRow(".lnk")]
    [DataRow(".url")]
    public void LaunchFilesDoNotLoadNativeShellMenuHandlers(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        File.WriteAllText(path, "fixture");
        try { Assert.IsFalse(new ShellMenuActionProvider().CanProvide(new[] { new FakeResult { FullPath = path } })); }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ApplicationProvidersKeepTheBuiltInActionsWithoutANativeMenu()
    {
        using var file = new TempFile();
        Assert.IsFalse(new ShellMenuActionProvider().CanProvide(new[] { new FakeResult { FullPath = file.Path, IsApplication = true } }));
    }

    [TestMethod]
    public void CanProvide_EmptyResults_ReturnsFalse() =>
        Assert.IsFalse(new ShellMenuActionProvider().CanProvide(Array.Empty<ISearchResult>()));

    [TestMethod]
    public void CanProvide_MultipleResults_ReturnsFalse()
    {
        using var file = new TempFile();
        var results = new ISearchResult[] { new FakeResult { FullPath = file.Path }, new FakeResult { FullPath = file.Path } };

        Assert.IsFalse(new ShellMenuActionProvider().CanProvide(results));
    }

    [TestMethod]
    public void CanProvide_SingleExistingFile_ReturnsTrue()
    {
        using var file = new TempFile();

        Assert.IsTrue(new ShellMenuActionProvider().CanProvide(new ISearchResult[] { new FakeResult { FullPath = file.Path } }));
    }

    [TestMethod]
    public void CanProvide_SingleNonExistentPath_ReturnsFalse() =>
        Assert.IsFalse(new ShellMenuActionProvider().CanProvide(new ISearchResult[] { new FakeResult { FullPath = @"Z:\gone.txt" } }));

    [TestMethod]
    public void CanProvide_EmptyPath_ReturnsFalse() =>
        Assert.IsFalse(new ShellMenuActionProvider().CanProvide(new ISearchResult[] { new FakeResult { FullPath = "" } }));
}
