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
    public void SearchingUsesBothTheAliasAndOriginalName()
    {
        var group = new QuickPanelGroupViewModel("source", "Source", "", new() { (new AppSearchResult { Name = "original", FullPath = "file" }, null) });
        group.ItemLabels.Set("file", "shortcut", false);
        group.ApplyFilter("shortcut"); Assert.AreEqual(1, group.Count);
        group.ApplyFilter("original"); Assert.AreEqual(1, group.Count);
        group.ApplyFilter("missing"); Assert.AreEqual(0, group.Count);
        group.ApplyFilter("");
        Assert.AreEqual("original", group.Items[0].Name);
    }
}
