using Lertaro.Core.Services.Search;
using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.Services.Search;

[TestClass]
public sealed class RecentFileExclusionTests
{
    [TestMethod]
    public void FilterRecentFiles_AppliesExclusionsBeforeLimit()
    {
        var rules = ExclusionRuleSet.From(new UserSettings
        {
            ExcludedPaths = [@"C:\Hidden"],
            IgnoredPathGlobs = [],
            IgnoredPathRegexes = []
        });
        var local = new SearchResult { Path = @"C:\Hidden\newest.txt" };
        var network = new SearchResult { Path = @"D:\Visible\next.txt" };

        var results = SearchService.FilterRecentFiles([local], [network], limit: 1, rules);

        Assert.HasCount(1, results);
        Assert.AreEqual(network.Path, results[0].Path);
    }

    [TestMethod]
    public void PathModeSearch_ExclusionsAreBypassedOnlyWhenExplicitlyRequested()
    {
        var parsed = SearchQueryParser.Parse(@"C:\Hidden\atlas.txt");
        Assert.IsTrue(parsed.IsPathMode);

        var rules = ExclusionRuleSet.From(new UserSettings
        {
            ExcludedPaths = [@"C:\Hidden"],
            IgnoredPathGlobs = [],
            IgnoredPathRegexes = []
        });
        var result = new SearchResult { Path = @"C:\Hidden\atlas.txt" };

        Assert.IsTrue(SearchService.ShouldExcludeByUserRules(result, rules, directoryFilter: null, explicitlyBypassed: false));
        Assert.IsFalse(SearchService.ShouldExcludeByUserRules(result, rules, directoryFilter: null, explicitlyBypassed: true));
    }
}
