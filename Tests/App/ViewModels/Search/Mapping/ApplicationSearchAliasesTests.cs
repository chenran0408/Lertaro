using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class ApplicationSearchAliasesTests
{
    [TestMethod]
    [DataRow("Visual Studio Code")]
    [DataRow("VisualStudioCode")]
    [DataRow("Microsoft Visual Studio Code")]
    public void EnglishWordAbbreviationsIncludeVscode(string title)
    {
        var aliases = ApplicationSearchAliases.Collect(title, @"C:\Apps\Code.exe", _ => null);
        Assert.IsTrue(aliases.Any(alias => alias.Equals("vscode", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(aliases.Any(alias => alias.Equals("Code", StringComparison.OrdinalIgnoreCase)));
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
    public void FullPinyinAndInitialsCanMatchAcrossSyllableAndWordBoundaries()
    {
        var aliases = new[] { "sakurafrp qi\u0002dong\u0002qi", "sakurafrp qdq|sakurafrp zcq" };
        Assert.IsTrue(ApplicationSearchAliases.IsExactMatch("SakuraFrp qidongqi", aliases, ['\u0002']));
        Assert.IsTrue(ApplicationSearchAliases.IsExactMatch("sakurafrpqdq", aliases, ['\u0002']));
        Assert.IsFalse(ApplicationSearchAliases.IsExactMatch("sakurafrpqd", aliases, ['\u0002']));
        Assert.IsFalse(ApplicationSearchAliases.IsExactMatch("", aliases, ['\u0002']));
    }
}
