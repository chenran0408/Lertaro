using Lertaro.Plugins.CoreExtensions.Providers.Indexing;
namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.Indexing;
[TestClass]
public sealed class ApplicationCatalogEntriesTests
{
    [TestMethod]
    public void LaunchKeepsExecutableArgumentsAndExplicitWorkingDirectorySeparate()
    {
        var start = ApplicationCatalogEntries.BuildStartInfo(@"D:\Apps with spaces\App.exe", "--profile \"Work profile\"", @"D:\Work with spaces");
        Assert.AreEqual(@"D:\Apps with spaces\App.exe", start.FileName);
        Assert.AreEqual("--profile \"Work profile\"", start.Arguments);
        Assert.AreEqual(@"D:\Work with spaces", start.WorkingDirectory);
        Assert.IsTrue(start.UseShellExecute);
    }
    [TestMethod]
    public void DefaultWorkingDirectoryIsTheExecutableFolder() =>
        Assert.AreEqual(@"D:\Portable", ApplicationCatalogEntries.BuildStartInfo(@"D:\Portable\App.exe", "", "").WorkingDirectory);
}
