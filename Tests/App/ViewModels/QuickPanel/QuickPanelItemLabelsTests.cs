using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

[TestClass]
public sealed class QuickPanelItemLabelsTests
{
    [TestMethod]
    public void LabelsAreTrimmedCaseInsensitiveAndIndependentOfOriginalFiles()
    {
        var original = new Dictionary<string, QuickPanelItemLabel> { ["file"] = new() { DisplayName = "old" } };
        var labels = new QuickPanelItemLabels(original);
        labels.Set("FILE", "  Short name  ", true);
        Assert.AreEqual("Short name", labels.Name("file", "original"));
        Assert.IsTrue(labels.Values["file"].HideName);
        Assert.AreEqual("old", original["file"].DisplayName);
        labels.Set("file", "", false);
        Assert.AreEqual("original", labels.Name("file", "original"));
        Assert.HasCount(0, labels.Values);
    }
    [TestMethod]
    [DataRow("Report.docx", false, "Report")]
    [DataRow("Archive.tar.gz", false, "Archive.tar")]
    [DataRow("Program.lnk", false, "Program")]
    [DataRow("Docs.v2", true, "Docs.v2")]
    [DataRow(".gitignore", false, ".gitignore")]
    [DataRow("README", false, "README")]
    public void DefaultNamesHideOnlyTheFileExtension(string name, bool directory, string expected)
        => Assert.AreEqual(expected, new QuickPanelItemLabels().Name(@"C:\Data\" + name, name, directory));

    [TestMethod]
    public void AliasesAndAlreadyFormattedNamesKeepTheirDots()
    {
        var labels = new QuickPanelItemLabels();
        labels.Set(@"C:\Data\Program.lnk", "App v1.2", false);
        Assert.AreEqual("App v1.2", labels.Name(@"C:\Data\Program.lnk", "Program.lnk"));
        Assert.AreEqual("App v1.2", labels.Name(@"C:\Data\Other.lnk", "App v1.2"));
    }

    [TestMethod]
    public void SearchingUsesBothTheAliasAndOriginalName()
    {
        var group = new QuickPanelGroupViewModel("source", "Source", "", new() { (new AppSearchResult { Name = "original.docx", FullPath = "file.docx" }, null) });
        group.ItemLabels.Set("file.docx", "shortcut", false);
        group.ApplyFilter("shortcut"); Assert.AreEqual(1, group.Count);
        group.ApplyFilter("original"); Assert.AreEqual(1, group.Count);
        group.ApplyFilter("docx"); Assert.AreEqual(1, group.Count);
        group.ApplyFilter("missing"); Assert.AreEqual(0, group.Count);
        group.ApplyFilter("");
        Assert.AreEqual("original.docx", group.Items[0].Name);
    }
}
