using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class RunningProcessCatalogTests
{
    [TestMethod]
    public void Normalize_DeduplicatesAndKeepsWindowedRepresentativeFirst()
    {
        var items = RunningProcessCatalog.Normalize([
            new("browser.exe", "", false), new("BROWSER.exe", "Browser window", true),
            new("worker.exe", "", false), new("editor.exe", "Document", true), new("", "", true)]);
        CollectionAssert.AreEqual(new[] { "BROWSER.exe", "editor.exe", "worker.exe" }, items.Select(x => x.Name).ToList());
        Assert.AreEqual("Browser window", items[0].WindowTitle);
    }

    [TestMethod]
    [DataRow(" EDITOR ", false, true)]
    [DataRow("document", false, true)]
    [DataRow("missing", true, false)]
    [DataRow("", false, true)]
    public void Matches_SearchesProcessAndTitleIgnoringCase(string search, bool showAll, bool matches) =>
        Assert.AreEqual(matches, RunningProcessCatalog.Matches(new("editor.exe", "Document", true), search, showAll));

    [TestMethod]
    public void Matches_BackgroundProcessesRequireShowAll()
    {
        var item = new RunningProcessInfo("worker.exe", "", false);
        Assert.IsFalse(RunningProcessCatalog.Matches(item, "", false));
        Assert.IsTrue(RunningProcessCatalog.Matches(item, "WORKER", true));
    }
}
