using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class ApplicationSearchAliasesTests
{
    [TestMethod]
    [DataRow("Visual Studio Code")]
    [DataRow("VisualStudioCode")]
    [DataRow("Microsoft Visual Studio Code")]
    [DataRow("Visual Studio Code (D drive)")]
    [DataRow("Visual Studio Code（便携版）")]
    [DataRow("Visual Studio Code [x64] (Portable)")]
    public void EnglishWordAbbreviationsIncludeVscode(string title)
    {
        var aliases = ApplicationSearchAliases.Collect(title, @"C:\Apps\Code.exe", _ => null);
        Assert.IsTrue(aliases.Any(alias => alias.Equals("vscode", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(aliases.Any(alias => alias.Equals("Code", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void InitialsCanOmitTheEditionOrYear()
    {
        var aliases = ApplicationSearchAliases.Collect("Copper Finch Studio 2026", @"C:\Apps\CopperFinch.exe", _ => null);
        Assert.Contains("CFS", aliases);
    }

    [TestMethod]
    public void ExecutableNameIsAnAliasForItsFriendlyApplicationTitle()
    {
        var aliases = ApplicationSearchAliases.Collect("微信", @"C:\Links\Weixin.lnk", _ => new(@"C:\Apps\Weixin.exe", "", ""));
        CollectionAssert.AreEqual(new[] { "Weixin" }, aliases);
    }

    [TestMethod]
    [DataRow(@"C:\Windows\cmd.exe")]
    [DataRow(@"C:\Windows\explorer.exe")]
    [DataRow(@"C:\Apps\javaw.exe")]
    [DataRow(@"C:\Work\document.docx")]
    [DataRow(@"C:\Work\Folder")]
    public void SharedHostsAndNonExecutableLinksDoNotBecomeApplicationNameAliases(string target)
    {
        var aliases = ApplicationSearchAliases.Collect("工作区", @"C:\Links\Workspace.lnk", _ => new(target, "", ""));
        Assert.HasCount(0, aliases);
    }

    [TestMethod]
    public void CatalogApplicationCanBeFoundByItsCompleteShortcutFilename()
    {
        var item = new SearchableItem { Title = "Paint", ResultKind = "Application", ActionArgument = @"C:\Menu\Drawing 2026.lnk" };
        var entry = new SearchableItemCache.CacheEntry(item, [], null);
        MatchRank Match(string query) => SearchableItemMapper.MatchCatalogEntry(FuzzyQuery.Parse(query), query.Split(' '), entry);
        Assert.AreEqual(new MatchRank(MatchRank.TierName, 0, 1), Match("Drawing 2026.lnk"));
        Assert.IsFalse(Match("Other 2026.lnk").IsMatch);
    }

    [TestMethod]
    public void FullPinyinAndInitialsCanMatchAcrossSyllableAndWordBoundaries()
    {
        var aliases = new[] { "sakurafrp qi\u0002dong\u0002qi", "sakurafrp qdq|sakurafrp zcq" };
        var exact = ApplicationSearchAliases.BuildExactAliases(aliases, ['\u0002']);
        bool Matches(string text) => exact.Contains(ApplicationSearchAliases.Normalize(text, ['\u0002']));
        Assert.IsTrue(Matches("SakuraFrp qidongqi"));
        Assert.IsTrue(Matches("sakurafrpqdq"));
        Assert.IsFalse(Matches("sakurafrpqd"));
        Assert.IsFalse(Matches(""));
    }
    [TestMethod]
    [DataRow("Microsoft Word", "Word")]
    [DataRow("AutoCAD 2026", "AutoCAD")]
    [DataRow("JetBrains IntelliJ IDEA", "IDEA")]
    public void ApplicationWordsRemainSearchableWhenExecutableNamesDiffer(string title, string query)
    {
        var aliases = ApplicationSearchAliases.Collect(title, @"C:\Apps\internal.exe", _ => null);
        Assert.IsTrue(aliases.Any(alias => alias.Equals(query, StringComparison.OrdinalIgnoreCase)));
    }

}
