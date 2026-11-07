using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class QueryFilterSupportTests
{
    private static ISearchResult Row(string fullPath, bool isDir = false, DateTime? modified = null) => new SearchResult
    {
        Path = fullPath,
        IsDir = isDir,
        Metadata = new FileMetadata { Modified = modified ?? DateTime.MinValue },
    };

    private static ResultFilters Parse(string query, out string cleaned)
    {
        cleaned = QueryFilterSupport.Extract(query, out var filters);
        QueryFilterSupport.Activate(filters);
        return filters;
    }

    [TestMethod]
    public void Extract_SingleKeyword_RemovesItAndBuildsFilter()
    {
        var filters = Parse("report folder:d:\\docs", out var cleaned);

        Assert.AreEqual("report", cleaned);
        Assert.AreEqual("d:\\docs", filters.FolderPrefix);
        Assert.IsFalse(filters.IsEmpty);
    }

    [TestMethod]
    public void Extract_MultipleKeywords_ComposesAll()
    {
        var filters = Parse("report folder:d:\\docs ext:pdf,docx file: datemodified:thisweek", out var cleaned);

        Assert.AreEqual("report", cleaned);
        Assert.AreEqual("d:\\docs", filters.FolderPrefix);
        Assert.IsTrue(filters.FilesOnly);
        Assert.IsNotNull(filters.Extensions);
        Assert.Contains("pdf", filters.Extensions);
        Assert.Contains("docx", filters.Extensions);
        Assert.AreEqual(ResultDateRange.ThisWeek, filters.DateRange);
    }

    [TestMethod]
    public void Extract_QuotedPathWithSpaces_KeepsTheWholePath()
    {
        var filters = Parse(@"report folder:""d:\my docs""", out var cleaned);

        Assert.AreEqual("report", cleaned);
        Assert.AreEqual(@"d:\my docs", filters.FolderPrefix);
    }

    [TestMethod]
    public void Extract_CaseInsensitiveKeys()
    {
        var filters = Parse("report FILE: Folder:d:\\x", out var cleaned);

        Assert.AreEqual("report", cleaned);
        Assert.IsTrue(filters.FilesOnly);
        Assert.AreEqual("d:\\x", filters.FolderPrefix);
    }

    [TestMethod]
    public void Extract_MonthPrefix_ValidatesShape()
    {
        var good = Parse("log datemodified:2026-11", out _);
        Assert.AreEqual(ResultDateRange.MonthPrefix, good.DateRange);
        Assert.AreEqual("2026-11", good.MonthPrefix);

        var bad = Parse("log datemodified: novemberish", out var cleaned);
        Assert.AreEqual(ResultDateRange.None, bad.DateRange);
        // An unknown value is left in the text: filtering nothing, matching nothing by surprise.
        StringAssert.Contains(cleaned, "novemberish");
    }

    [TestMethod]
    public void Extract_NoKeywords_QueryUntouched_AndFilterEmpty()
    {
        var filters = Parse("plain report", out var cleaned);
        Assert.AreEqual("plain report", cleaned);
        Assert.IsTrue(filters.IsEmpty);
    }

    [TestMethod]
    public void Matches_FilesOnly_DropsDirectories()
    {
        var filters = Parse("x file:", out _);
        Assert.IsFalse(filters.Matches(Row(@"c:\dir", isDir: true)));
        Assert.IsTrue(filters.Matches(Row(@"c:\file.txt")));
    }

    [TestMethod]
    public void Matches_Extensions_CaseInsensitive_AndExcludesDirectories()
    {
        var filters = Parse("x ext:PDF,Ddoc", out _);
        Assert.IsTrue(filters.Matches(Row(@"c:\a.PdF")));
        Assert.IsFalse(filters.Matches(Row(@"c:\a.txt")));
        Assert.IsFalse(filters.Matches(Row(@"c:\pdfdir", isDir: true)));
    }

    [TestMethod]
    public void Matches_Today_IsLocalDayBoundary_NotRolling24h()
    {
        var atMidnight = Parse("x datemodified:today", out _);
        var today = DateTime.Today;
        var lateYesterday = today.AddHours(-1); // 23:59 yesterday -- outside a rolling day, inside rolling 24h

        Assert.IsFalse(atMidnight.Matches(Row(@"c:\y.txt", modified: lateYesterday)));
        Assert.IsTrue(atMidnight.Matches(Row(@"c:\t.txt", modified: today.AddMinutes(1))));
    }

    [TestMethod]
    public void Matches_ThisWeek_StartsMonday()
    {
        var filters = Parse("x datemodified:thisweek", out _);
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        Assert.IsTrue(filters.Matches(Row(@"c:\w.txt", modified: monday)));
        Assert.IsFalse(filters.Matches(Row(@"c:\w.txt", modified: monday.AddMinutes(-1))));
    }

    [TestMethod]
    public void Matches_MonthPrefix_FiltersByYearMonth()
    {
        var filters = Parse("x datemodified:2026-11", out _);
        Assert.IsTrue(filters.Matches(Row(@"c:\n.txt", modified: new DateTime(2026, 11, 8))));
        Assert.IsFalse(filters.Matches(Row(@"c:\o.txt", modified: new DateTime(2026, 10, 31))));
    }

    [TestMethod]
    public void Activate_EmptyFilter_ClearsAmbient()
    {
        QueryFilterSupport.Activate(new ResultFilters { FilesOnly = true });
        Assert.IsNotNull(QueryFilterSupport.ActiveFilters);

        QueryFilterSupport.Activate(new ResultFilters());
        Assert.IsNull(QueryFilterSupport.ActiveFilters);
    }

    [TestMethod]
    public void Clear_RemovesAmbientFilters()
    {
        var filters = Parse("x file:", out _);
        Assert.IsFalse(filters.IsEmpty);
        QueryFilterSupport.Activate(filters);
        Assert.IsNotNull(QueryFilterSupport.ActiveFilters);
        QueryFilterSupport.Clear();
        Assert.IsNull(QueryFilterSupport.ActiveFilters);
    }
}
