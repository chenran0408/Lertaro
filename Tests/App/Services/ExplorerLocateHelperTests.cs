using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// Which folder a "locate in Explorer" should end up showing. Only the decision is covered: the rest of
// the call is ShellWindows COM against a live Explorer window.
[TestClass]
public sealed class ExplorerLocateHelperTests
{
    [TestMethod]
    public void RevealFileUsesASeparateExplorerProcessAndQuotesTheSelectedPath()
    {
        var start = ExplorerLocateHelper.BuildExplorerReveal(@"C:\Folder with spaces\Steam.lnk");
        Assert.IsNotNull(start);
        Assert.IsFalse(start.UseShellExecute, "Never re-enter ShellExecute in the Lertaro process.");
        Assert.AreEqual("explorer.exe", System.IO.Path.GetFileName(start.FileName));
        Assert.AreEqual("/select,\"C:\\Folder with spaces\\Steam.lnk\"", start.Arguments);
    }

    [TestMethod]
    public void DriveRootOpensDirectlyWithoutTryingToSelectItsNonexistentParent()
    {
        var start = ExplorerLocateHelper.BuildExplorerReveal(@"C:\");
        Assert.IsNotNull(start);
        Assert.AreEqual(@"C:\", start.Arguments);
    }

    [TestMethod]
    [DataRow("shell:AppsFolder")]
    [DataRow("Steam.lnk")]
    public void VirtualAndRelativeTargetsKeepTheirOwnShellRoute(string path)
        => Assert.IsNull(ExplorerLocateHelper.BuildExplorerReveal(path));

    [TestMethod]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    public void LocateOrFallback_OnlyFallsBackAfterFailure(bool located, int expectedFallbacks)
    {
        var attempts = 0;
        var fallbacks = 0;
        ExplorerLocateHelper.LocateOrFallback(() => { attempts++; return located; }, () => fallbacks++);
        Assert.AreEqual(1, attempts);
        Assert.AreEqual(expectedFallbacks, fallbacks);
    }

    [TestMethod]
    public void ResolveContainingFolder_Folder_IsItsOwnTarget() =>
        Assert.AreEqual(
            @"C:\folder\sub",
            ExplorerLocateHelper.ResolveContainingFolder(@"C:\folder\sub", path => path == @"C:\folder\sub"));

    [TestMethod]
    public void ResolveContainingFolder_File_ResolvesToItsParent() =>
        Assert.AreEqual(@"C:\folder", ExplorerLocateHelper.ResolveContainingFolder(@"C:\folder\file.txt", _ => false));

    [TestMethod]
    [DataRow(@"C:\")]
    [DataRow("shell:AppsFolder")]
    [DataRow("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    public void ResolveContainingFolder_NothingToShow_ReportsNoFolder(string path) =>
        // A drive root and a virtual shell token have no containing folder, which is what routes them to
        // the shell-locate fallback instead of pretending there was one to open. Null or empty, exactly:
        // only "no folder" is promised, not which spelling of it comes back.
        Assert.IsTrue(
            string.IsNullOrEmpty(ExplorerLocateHelper.ResolveContainingFolder(path, _ => false)),
            $"'{path}' resolved to a containing folder");
}
