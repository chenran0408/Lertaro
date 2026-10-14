using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class LaunchCandidateOrderingTests
{
    private static SearchResultMapper.RankedCandidate Candidate(string name, string path, double weight = .55,
        bool history = false, bool application = false, bool directory = false) => new(
            new AppSearchResult { Name = name, FullPath = path, IsDir = directory, ResultKind = application ? "Application" : "File" },
            history, history ? 0 : int.MaxValue, int.MaxValue, new MatchRank(0, 0, weight), path);

    [TestMethod]
    public void ExactSteamLaunchBeatsSameNamedFoldersWithTheDefaultFilesFirstOrder()
    {
        var fuzzy = FuzzyQuery.Parse("steam");
        var folder = Candidate("Steam", @"C:\Apps\Steam", directory: true, history: true)
            with { TypeRank = 0, Match = fuzzy.Rank("Steam") };
        var executable = Candidate("steam.exe", @"C:\Apps\Steam\steam.exe")
            with { TypeRank = 0, Match = fuzzy.Rank("steam.exe") };
        var shortcut = Candidate("Steam", @"C:\StartMenu\Steam.lnk", application: true)
            with { TypeRank = 1, Match = fuzzy.Rank("Steam") };
        var results = LaunchCandidateOrdering.Order(new() { folder, executable, shortcut }, "steam",
            _ => new(@"C:\Apps\Steam\steam.exe", "", @"C:\Apps\Steam"));
        Assert.AreSame(shortcut.Result, results[0].Result);
        Assert.AreSame(folder.Result, results[1].Result);
        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void ExactExecutableBeatsSameNamedFolderEvenWithoutAnApplicationProvider()
    {
        var fuzzy = FuzzyQuery.Parse("steam");
        var folder = Candidate("Steam", @"C:\Apps\Steam", directory: true)
            with { Match = fuzzy.Rank("Steam") };
        var executable = Candidate("steam.exe", @"C:\Apps\Steam\steam.exe")
            with { Match = fuzzy.Rank("steam.exe") };
        Assert.AreSame(executable.Result, Order("steam", folder, executable)[0].Result);
    }

    [TestMethod]
    public void FilesFirstTypeOrderDoesNotOverrideLaunchPreferenceForSimilarMatches()
    {
        var file = Candidate("Editor notes", @"C:\Work\notes.txt") with { TypeRank = 0 };
        var application = Candidate("Editor Professional", @"shell:AppsFolder\editor", application: true)
            with { TypeRank = 1 };
        Assert.AreSame(application.Result, Order("edi", file, application)[0].Result);
    }

    [TestMethod]
    public void AStrongerFileMatchStillBeatsAFuzzyApplicationWithFilesOrderedLast()
    {
        var file = Candidate("Notes", @"C:\Work\Notes.txt", .85) with { TypeRank = 1 };
        var application = Candidate("Notes Tool", @"C:\Apps\NotesTool.exe", .4, application: true)
            with { TypeRank = 0 };
        Assert.AreSame(file.Result, Order("note", application, file)[0].Result);
    }

    [TestMethod]
    public void QQApplicationKeepsItsTitleAndLaunchCallbackInsteadOfTheIndexedShortcutRow()
    {
        var fuzzy = FuzzyQuery.Parse("qq");
        var link = Candidate("QQ.lnk", @"C:\StartMenu\QQ.lnk")
            with { TypeRank = 0, Match = fuzzy.Rank("QQ.lnk") };
        var application = Candidate("QQ", link.Result.FullPath, application: true)
            with { TypeRank = 1, Match = fuzzy.Rank("QQ") };
        application.Result.InstantResultOnExecute = () => { };
        var results = LaunchCandidateOrdering.Order(new() { link, application }, "qq",
            _ => new(@"C:\Apps\QQ\QQ.exe", "", @"C:\Apps\QQ"));
        Assert.AreSame(application.Result, Assert.ContainsSingle(results).Result);
        Assert.AreEqual("QQ", results[0].Result.Name);
        Assert.IsNotNull(results[0].Result.InstantResultOnExecute);
    }

    [TestMethod]
    public void CatalogApplicationWinsAnEquivalentCopiedShortcutEvenWhenItsPathIsLonger()
    {
        var link = Candidate("QQ.lnk", @"C:\Panel\QQ.lnk") with { TypeRank = 0 };
        var application = Candidate("QQ", @"C:\StartMenu\Programs\Tencent\QQ.lnk", application: true)
            with { TypeRank = 1 };
        var results = LaunchCandidateOrdering.Order(new() { link, application }, "qq",
            _ => new(@"C:\Apps\QQ\QQ.exe", "", ""));
        Assert.AreSame(application.Result, Assert.ContainsSingle(results).Result);
    }

    [TestMethod]
    public void FolderShortcutLabeledApplicationDoesNotTakeTheApplicationTitlePreference()
    {
        var file = Candidate("Workspace notes", @"C:\Work\notes.txt") with { TypeRank = 0 };
        var folder = Candidate("Workspace link", @"C:\Links\Workspace.lnk", application: true)
            with { TypeRank = 1 };
        var results = LaunchCandidateOrdering.Order(new() { folder, file }, "work",
            _ => new(@"C:\Work", "", ""));
        Assert.AreSame(file.Result, results[0].Result);
    }

    [TestMethod]
    [DataRow("wx", 0)]
    [DataRow("weixin", 2)]
    public void CompletePinyinAndInitialsMatchBeatsAnExactlyNamedFolder(string query, int tier)
    {
        var application = Candidate("微信", @"C:\StartMenu\Weixin.lnk", application: true)
            with { TypeRank = 1, Match = new MatchRank(tier, 0, 1) };
        var folder = Candidate(query, @"C:\Work\folder", 1, directory: true) with { TypeRank = 0 };
        var results = LaunchCandidateOrdering.Order(new() { folder, application }, query,
            _ => new(@"C:\Apps\Weixin.exe", "", ""));
        Assert.AreSame(application.Result, results[0].Result);
    }

    [TestMethod]
    public void PartialAliasDoesNotOverrideAnExactFileName()
    {
        var application = Candidate("微信工具", @"C:\Apps\WeixinTools.exe", .7, application: true);
        var file = Candidate("wx", @"C:\Work\wx.txt", .7);
        Assert.AreSame(file.Result, Order("wx", application, file)[0].Result);
    }

    [TestMethod]
    public void FullAliasCoverageDoesNotOverrideAnExplicitFileExtension()
    {
        var application = Candidate("微信", @"C:\Apps\Weixin.exe", 1, application: true) with { IsExactAliasMatch = true };
        var file = Candidate("wx.txt", @"C:\Work\wx.txt", .7);
        Assert.AreSame(file.Result, Order("wx.txt", application, file)[0].Result);
    }

    [TestMethod]
    public void AnExplicitlyRequestedSupportApplicationBeatsItsSameNamedFolder()
    {
        var application = Candidate("Uninstall Editor", @"C:\Apps\unins000.exe", 1, application: true);
        var folder = Candidate("Uninstall Editor", @"C:\Work\Uninstall Editor", 1, directory: true);
        Assert.AreSame(application.Result, Order("Uninstall Editor", folder, application)[0].Result);
    }

    [TestMethod]
    public void CompleteMixedLanguageAliasKeepsItsExactPreferenceDespiteWordAveragedWeight()
    {
        var application = Candidate("Office 语言首选项", @"C:\Apps\OfficeLanguage.exe", .46, application: true)
            with { IsExactAliasMatch = true };
        var folder = Candidate("officeyysxx", @"C:\Work\officeyysxx", 1, directory: true);
        Assert.AreSame(application.Result, Order("officeyysxx", folder, application)[0].Result);
    }

    [TestMethod]
    [DataRow("Lertaro.App")]
    [DataRow("Battle.net Launcher")]
    public void DotInAnExactApplicationAliasIsNotMistakenForARequestedFileExtension(string query)
    {
        var application = Candidate("Friendly name", @"C:\Apps\launcher.exe", 1, application: true)
            with { IsExactAliasMatch = true };
        var folder = Candidate(query, @"C:\Work\folder", 1, directory: true);
        Assert.AreSame(application.Result, Order(query, folder, application)[0].Result);
    }

    [TestMethod]
    public void ExactDocumentBeatsFuzzyApplicationAndOldHistory()
    {
        var exact = Candidate("report.docx", @"C:\Work\report.docx", .4);
        var app = Candidate("Report Designer", @"C:\Apps\report.exe", .9, application: true);
        var history = Candidate("old report.docx", @"C:\Work\old report.docx", .9, history: true);
        Assert.AreSame(exact.Result, Order("report.docx", history, app, exact)[0].Result);
    }

    [TestMethod]
    public void SimilarMatchesPreferApplicationEntryThenExeThenFilesThenSupportTools()
    {
        var file = Candidate("Editor notes", @"C:\Work\notes.txt");
        var exe = Candidate("Editor", @"C:\Apps\editor.exe");
        var app = Candidate("Editor Professional", @"shell:AppsFolder\editor", application: true);
        var updater = Candidate("Editor Updater", @"C:\Apps\EditorUpdater.exe");
        var result = Order("edi", updater, file, exe, app);
        CollectionAssert.AreEqual(new[] { app.Result, exe.Result, file.Result, updater.Result }, result.Select(item => item.Result).ToArray());
    }

    [TestMethod]
    public void MuchBetterMatchBeatsApplicationPreferenceAndHistory()
    {
        var file = Candidate("Notes", @"C:\Work\Notes.txt", .85);
        var app = Candidate("Notes Tool", @"C:\Apps\NotesTool.exe", .4, history: true, application: true);
        Assert.AreSame(file.Result, Order("note", app, file)[0].Result);
    }

    [TestMethod]
    public void HistoryHelpsCloseMatchesWithoutBeingAnAbsoluteTopTier()
    {
        var used = Candidate("Known", @"C:\Work\Known.txt", .51, history: true);
        var newFile = Candidate("Other", @"C:\Work\Other.txt", .59);
        Assert.AreSame(used.Result, Order("o", newFile, used)[0].Result);
    }

    [TestMethod]
    public void FolderAndDocumentLinksAreNotTreatedAsApplications()
    {
        var row = Candidate("Workspace", @"C:\Links\Workspace.lnk", application: true).Result;
        Assert.AreEqual(2, LaunchCandidateOrdering.LaunchRank(row, new(@"C:\Work", "", "")));
        Assert.AreEqual(2, LaunchCandidateOrdering.LaunchRank(row, new(@"C:\Work\notes.docx", "", "")));
        Assert.AreEqual(2, LaunchCandidateOrdering.LaunchRank(row, null));
        Assert.AreEqual(0, LaunchCandidateOrdering.LaunchRank(row, new(@"C:\Apps\Editor.exe", "", "")));
    }

    [TestMethod]
    public void EquivalentShortcutReplacesExeButArgumentsAndWorkingDirectoriesRemainDistinct()
    {
        var exe = Candidate("Editor", @"C:\Apps\Editor.exe");
        var shortcut = Candidate("Editor", @"C:\Links\Editor.lnk");
        var args = Candidate("Editor safe", @"C:\Links\Safe.lnk");
        var work = Candidate("Editor project", @"C:\Links\Project.lnk");
        LaunchShortcut? Read(string path) => path == shortcut.Result.FullPath ? new(@"C:\Apps\Editor.exe", "", @"C:\Apps")
            : path == args.Result.FullPath ? new(@"C:\Apps\Editor.exe", "--safe", "")
            : path == work.Result.FullPath ? new(@"C:\Apps\Editor.exe", "", @"C:\Project") : null;
        var results = LaunchCandidateOrdering.Order(new() { exe, shortcut, args, work }, "edi", Read);
        Assert.HasCount(3, results);
        Assert.IsFalse(results.Any(item => ReferenceEquals(item.Result, exe.Result)));
        Assert.IsTrue(results.Any(item => ReferenceEquals(item.Result, shortcut.Result)));
        Assert.IsTrue(results.Any(item => ReferenceEquals(item.Result, args.Result)));
        Assert.IsTrue(results.Any(item => ReferenceEquals(item.Result, work.Result)));
        Assert.AreEqual(shortcut.Result.FullPath, results.First().Result.FullPath);
    }

    [TestMethod]
    public void ShortcutReplacementRetainsTheBetterExecutableMatch()
    {
        var exe = Candidate("Editor.exe", @"C:\Apps\Editor.exe", .9);
        var link = Candidate("Launch Editor", @"C:\Links\Launch Editor.lnk", .3);
        var document = Candidate("Editor notes", @"C:\Work\Editor notes.txt", .7);
        var result = LaunchCandidateOrdering.Order(new() { document, link, exe }, "Editor",
            _ => new(@"C:\Apps\Editor.exe", "", ""));
        Assert.HasCount(2, result);
        Assert.AreSame(link.Result, result[0].Result);
    }

    [TestMethod]
    public void SameNamedProgramsWithDifferentTargetsAreNotMerged()
    {
        var a = Candidate("Editor", @"C:\A\Editor.exe");
        var b = Candidate("Editor", @"C:\B\Editor.exe");
        Assert.HasCount(2, Order("edi", a, b));
    }

    [TestMethod]
    public void ExplicitExeExtensionKeepsTheRequestedExecutable()
    {
        var exe = Candidate("Editor.exe", @"C:\Apps\Editor.exe");
        var shortcut = Candidate("Editor", @"C:\Links\Editor.lnk");
        var result = LaunchCandidateOrdering.Order(new() { shortcut, exe }, "Editor.exe", _ => new(@"C:\Apps\Editor.exe", "", ""));
        Assert.AreSame(exe.Result, result[0].Result);
        Assert.HasCount(2, result);
    }

    [TestMethod]
    [DataRow(false, null, false, false, null, true)]
    [DataRow(true, null, false, false, null, false)]
    [DataRow(false, @"C:\Work", false, false, null, false)]
    [DataRow(false, null, true, false, null, false)]
    [DataRow(false, null, false, true, null, false)]
    [DataRow(false, null, false, false, "Files", false)]
    public void LaunchOrderingOnlyAppliesToUnscopedGlobalQueries(bool inline, string? scope, bool filtered,
        bool foldersOnly, string? trigger, bool expected)
        => Assert.AreEqual(expected, LaunchCandidateOrdering.Applies(inline, scope, filtered, foldersOnly, trigger));

    [TestMethod]
    public void SameExecutableWithDifferentParametersAndWorkingFoldersRemainsSelectable()
    {
        var normal = Candidate("Editor", @"C:\Apps\Editor.exe", application: true);
        var work = Candidate("Editor", normal.Result.FullPath, application: true);
        work.Result.ApplicationArguments = "--profile Work";
        var lower = Candidate("Editor", normal.Result.FullPath, application: true);
        lower.Result.ApplicationArguments = "--profile work";
        var folder = Candidate("Editor", normal.Result.FullPath, application: true);
        folder.Result.ApplicationWorkingDirectory = @"C:\Projects";
        Assert.HasCount(4, Order("Editor", normal, work, lower, folder));
    }

    [TestMethod]
    public void ShortcutWithCustomWorkingFolderRemainsSeparateFromDefaultLaunch()
    {
        var normal = Candidate("Editor", @"C:\Links\Editor.lnk", application: true);
        var custom = Candidate("Work editor", normal.Result.FullPath, application: true);
        custom.Result.ApplicationWorkingDirectory = @"C:\Projects";
        Assert.HasCount(2, LaunchCandidateOrdering.Order([normal, custom], "edi", _ => new(@"C:\Apps\Editor.exe", "", @"C:\Apps")));
    }

    [TestMethod]
    public void WholeWordFileBeatsScatteredApplicationWithoutChangingExactLaunchPreference()
    {
        var fuzzy = FuzzyQuery.Parse("atlas");
        var whole = Candidate("code-atlas", @"C:\Work\code-atlas.txt") with { Match = fuzzy.Rank("code-atlas") };
        var scattered = Candidate("AutofillStates", @"C:\Apps\AutofillStates.exe", application: true)
            with { Match = fuzzy.Rank("AutofillStates") };
        Assert.AreSame(whole.Result, Order("atlas", scattered, whole)[0].Result);
    }

    [TestMethod]
    public void ComparableFilesPreferRecentThenShallowPaths()
    {
        var old = Candidate("report-a", @"C:\Work\a.txt") with { RecencyBand = RankSignals.BandOlder, Depth = 1 };
        var deep = Candidate("report-b", @"C:\Work\Deep\b.txt") with { RecencyBand = RankSignals.BandToday, Depth = 9 };
        var shallow = Candidate("report-c", @"C:\Work\c.txt") with { RecencyBand = RankSignals.BandToday, Depth = 3 };
        var results = Order("rep", old, deep, shallow);
        Assert.AreSame(shallow.Result, results[0].Result);
        Assert.AreSame(deep.Result, results[1].Result);
        Assert.AreSame(old.Result, results[2].Result);
    }

    private static List<SearchResultMapper.RankedCandidate> Order(string query, params SearchResultMapper.RankedCandidate[] candidates)
        => LaunchCandidateOrdering.Order(candidates.ToList(), query, _ => null);
}
