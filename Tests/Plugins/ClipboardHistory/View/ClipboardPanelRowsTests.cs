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
            entry => $"sub:{entry.PlainPreview}", _ => dateLabel,
            entry => $"tip:{entry.PlainPreview}", pinnedLabel);

    [TestMethod]
    public void Build_EmptyHistory_EmptyList()
    {
        Assert.IsEmpty(Build([]));
    }

    [TestMethod]
    public void Build_NumberSlotsOnTheFirstNineRows_Only()
    {
        var entries = Enumerable.Range(1, 12).Select(i => Text($"row {i}", i)).ToList();
        var items = Build(entries);

        var slots = items.Where(item => item.IsRow).Select(item => item.SlotText).ToList();
        Assert.HasCount(12, slots);
        CollectionAssert.AreEqual(
            new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "", "", "" }, slots);
    }

    [TestMethod]
    public void Build_SlotBadgesMatchSelectableIds()
    {
        // Headers split the list; the badge walk and the 1~9 paste walk must agree.
        var entries = new List<ClipboardEntry>
        {
            Text("pinned", 1, pinned: true), Text("a", 2), Text("b", 3), Text("c", 4),
        };
        var items = Build(entries);

        var badged = items.Where(item => item.IsRow && item.SlotText.Length > 0)
            .Select(item => item.Id).ToList();
        var selectable = ClipboardPanelRows.SelectableIds(items).Take(9).ToList();
        CollectionAssert.AreEqual(selectable, badged);
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
    public void Sort_ByFrequency_CountsPastesWithCaptures()
    {
        // Two captures vs one capture + three pastes: "most used" ranks the pasted row first,
        // because the frequency toggle is about how often the entry is actually used.
        var entries = new List<ClipboardEntry>
        {
            Text("captured twice", 1, seen: 2),
            Text("captured once, pasted thrice", 2, seen: 1),
        };
        entries[1] = entries[1] with { PasteCount = 3 };

        var byFrequency = ClipboardPanelRows.Sort(entries, byFrequency: true);
        Assert.AreEqual(2, byFrequency[0].Id);
        Assert.AreEqual(1, byFrequency[1].Id);

        // Recency ignores the paste tally entirely.
        var byRecency = ClipboardPanelRows.Sort(entries, byFrequency: false);
        Assert.AreEqual(1, byRecency[0].Id); // store order preserved
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

    [TestMethod]
    public void RowIndexesBetween_SpansAnchorToCursor_SkipsHeaders()
    {
        var items = new List<ClipboardPanelItem>
        {
            ClipboardPanelItem.Header("pinned"),                             // 0
            new(false, "", 11, "a", "", ClipboardEntryKind.Text, "", true),  // 1
            new(false, "", 12, "b", "", ClipboardEntryKind.Text, "", false), // 2
            ClipboardPanelItem.Header("today"),                              // 3
            new(false, "", 13, "c", "", ClipboardEntryKind.Text, "", false), // 4
        };

        CollectionAssert.AreEqual(new[] { 1, 2 }, ClipboardPanelRows.RowIndexesBetween(1, 2, items));
        // Order-free: growing upwards from the cursor covers the same rows.
        CollectionAssert.AreEqual(new[] { 1, 2, 4 }, ClipboardPanelRows.RowIndexesBetween(4, 1, items));
        // A header end (defensive: callers seed the anchor from a row) does not truncate the span.
        CollectionAssert.AreEqual(new[] { 1, 2, 4 }, ClipboardPanelRows.RowIndexesBetween(0, 4, items));
        // A cursor on a header or past the list extends nothing.
        Assert.IsEmpty(ClipboardPanelRows.RowIndexesBetween(1, 3, items));
        Assert.IsEmpty(ClipboardPanelRows.RowIndexesBetween(1, 9, items));
        Assert.IsEmpty(ClipboardPanelRows.RowIndexesBetween(-1, 2, items));
    }
}
