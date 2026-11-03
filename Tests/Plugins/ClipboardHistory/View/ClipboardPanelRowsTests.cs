namespace Lertaro.Plugins.ClipboardHistory.Tests.View;
using Lertaro.Plugins.ClipboardHistory.View;

[TestClass]
public sealed class ClipboardPanelRowsTests
{
    private static ClipboardEntry Text(string preview, long id = 1, bool pinned = false,
        ClipboardEntryKind kind = ClipboardEntryKind.Text, long seen = 1) =>
        new(id, preview, preview, "app", "title", pinned, seen,
            CreatedAtMs: 1_700_000_000_000, LastSeenAtMs: 1_700_000_000_000, Kind: kind);

    private static readonly Func<string, string, bool> Contains =
        (text, query) => text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static List<ClipboardPanelItem> Build(IReadOnlyList<ClipboardEntry> entries,
        ClipboardPanelTab tab = ClipboardPanelTab.All, string query = "",
        string dateLabel = "2026/11/03", string pinnedLabel = "pinned") =>
        ClipboardPanelRows.Build(entries, tab, query, Contains,
            entry => $"sub:{entry.PlainPreview}", _ => dateLabel, pinnedLabel);

    [TestMethod]
    public void Build_EmptyHistory_EmptyList()
    {
        Assert.IsEmpty(Build([]));
    }

    [TestMethod]
    public void Build_InsertsHeaderBeforeFirstRowOfEachGroup()
    {
        var items = Build([
            Text("pinned one", 1, pinned: true),
            Text("recent", 2),
        ], pinnedLabel: "PINNED", dateLabel: "TODAY");

        Assert.AreEqual(4, items.Count);
        Assert.AreEqual("PINNED", items[0].HeaderText);
        Assert.IsTrue(items[0].IsHeader);
        Assert.AreEqual("pinned one", items[1].Title);
        Assert.AreEqual("TODAY", items[2].HeaderText);
        Assert.AreEqual("recent", items[3].Title);
    }

    [TestMethod]
    public void Build_TabFiltersByKind()
    {
        var entries = new List<ClipboardEntry>
        {
            Text("t", 1),
            Text("img", 2, kind: ClipboardEntryKind.Image, pinned: true),
            Text("f", 3, kind: ClipboardEntryKind.FilePaths),
        };

        Assert.AreEqual(3, Build(entries, ClipboardPanelTab.All).Count(item => item.IsRow));
        Assert.AreEqual(1, Build(entries, ClipboardPanelTab.Text).Count(item => item.IsRow));
        // Favorites shows the pinned image even on the Images tab; here only the tab filter runs.
        Assert.AreEqual(1, Build(entries, ClipboardPanelTab.Image).Count(item => item.IsRow));
        Assert.AreEqual(1, Build(entries, ClipboardPanelTab.Files).Count(item => item.IsRow));
        Assert.AreEqual(1, Build(entries, ClipboardPanelTab.Favorites).Count(item => item.IsRow));
    }

    [TestMethod]
    public void Build_QueryMatchesPreviewOrContent()
    {
        var entries = new List<ClipboardEntry>
        {
            Text("hello world", 1),
        };
        entries.Add(Text("placeholder", 2, kind: ClipboardEntryKind.FilePaths)
            with { PlainPreview = "2 · report.docx", Content = @"C:\docs\report.docx" });

        // "hello" lives in the preview; "report.docx" only in the (file list) content.
        Assert.AreEqual(1, Build(entries, query: "hello").Count(item => item.IsRow));
        Assert.AreEqual(1, Build(entries, query: "report.docx").Count(item => item.IsRow));
        Assert.AreEqual(0, Build(entries, query: "nomatch").Count(item => item.IsRow));
    }

    [TestMethod]
    public void Sort_ByFrequency_PinnedFirstThenMostCopied()
    {
        var entries = new List<ClipboardEntry>
        {
            Text("seen1", 1, seen: 1),
            Text("seen5", 2, seen: 5),
            Text("pinned seen1", 3, pinned: true, seen: 1),
        };

        var byRecency = ClipboardPanelRows.Sort(entries, byFrequency: false);
        Assert.AreEqual(1, byRecency[0].Id); // store order preserved

        var byFrequency = ClipboardPanelRows.Sort(entries, byFrequency: true);
        Assert.AreEqual(3, byFrequency[0].Id); // pinned still first
        Assert.AreEqual(2, byFrequency[1].Id); // then highest seen count
        Assert.AreEqual(1, byFrequency[2].Id);
    }

    [TestMethod]
    public void SelectableIds_SkipsHeadersAndKeepsDisplayOrder()
    {
        var items = new List<ClipboardPanelItem>
        {
            ClipboardPanelItem.Header("pinned"),
            new(false, "", 11, "a", "", ClipboardEntryKind.Text, "", true),
            ClipboardPanelItem.Header("today"),
            new(false, "", 12, "b", "", ClipboardEntryKind.Text, "", false),
        };

        CollectionAssert.AreEqual(new[] { 11L, 12L }, ClipboardPanelRows.SelectableIds(items));
    }

    [TestMethod]
    public void MatchesQuery_EmptyQuery_MatchesEverything()
    {
        Assert.IsTrue(ClipboardPanelRows.MatchesQuery(Text("anything"), "", Contains));
        Assert.IsTrue(ClipboardPanelRows.MatchesQuery(Text(""), "", Contains));
    }
}
