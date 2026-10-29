using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.QuickPanel;

// What a plugin-provided quick panel source turns into. The load itself goes through PluginManager and
// is not reachable from here; these cover the two decisions it makes on the way -- how a plugin's own
// ISearchResult becomes a row, and what "newest first" means for a provider that has no timestamps.
[TestClass]
public sealed class QuickPanelPluginGroupTests
{
    [TestMethod]
    public async Task SavedPluginGroupChoicesFollowTheFolderWhenProviderOrderChanges()
    {
        var settings = new Core.QuickPanelSettings();
        settings.PluginGroupPreferences[@"plugin:C:\Projects"] = new Core.QuickPanelGroupPreference
        {
            Sort = Core.QuickPanelSortMode.Custom, ThumbnailView = false, Expanded = false,
            ItemOrder = new() { @"C:\Projects\z.txt", @"C:\Projects\a.txt" }
        };
        var provider = new Provider();
        var vm = new QuickPanelViewModel(() => settings, saveSettings: () => { });
        var groups = new List<QuickPanelGroupViewModel>();
        await vm.LoadPluginTabAsync(provider, "plugin", "Plugin", (group, _) => groups.Add(group), CancellationToken.None);
        var saved = groups.Single(group => group.FolderPath == @"C:\Projects");
        Assert.AreEqual("z.txt", saved.Items[0].Name);
        Assert.IsFalse(saved.IsThumbnailView);
        Assert.IsFalse(saved.IsExpanded);
        provider.Reverse = true;
        groups.Clear();
        await vm.LoadPluginTabAsync(provider, "plugin", "Plugin", (group, _) => groups.Add(group), CancellationToken.None);
        Assert.AreEqual(saved.SourceId, groups.Single(group => group.FolderPath == @"C:\Projects").SourceId);
    }

    [TestMethod]
    public async Task PluginTab_FiltersGloballyExcludedEntryPaths()
    {
        var rules = Core.ExclusionRuleSet.From(new Core.UserSettings { ExcludedPaths = new() { @"C:\blocked" } }, @"C:\");
        var vm = new QuickPanelViewModel(() => new Core.QuickPanelSettings(), saveSettings: () => { }, exclusionRules: () => rules);
        var groups = new List<QuickPanelGroupViewModel>();
        await vm.LoadPluginTabAsync(new Provider(), "plugin", "Plugin", (group, _) => groups.Add(group), CancellationToken.None);

        var items = groups.SelectMany(group => group.Items).ToList();
        CollectionAssert.AreEqual(new[] { "a.txt", "Example.App!App", "file.txt", "z.txt" }, items.Select(item => item.Name).OrderBy(name => name).ToArray());
        Assert.IsFalse(items.Any(item => item.FullPath.StartsWith(@"C:\blocked", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(items.Any(item => item.FullPath == @"shell:AppsFolder\Example.App!App"));
    }

    private sealed class Provider : PluginSdk.Abstractions.Plugins.IQuickPanelTabProvider
    {
        public bool Reverse { get; set; }
        public Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
        {
            IEnumerable<ISearchResult> entries = new ISearchResult[]
            {
                new PluginItem(@"C:\Other\file.txt"), new PluginItem(@"C:\Projects\a.txt"), new PluginItem(@"C:\Projects\z.txt"), new PluginItem(@"C:\blocked\secret.txt"),
                new PluginItem(@"shell:AppsFolder\Example.App!App", isApplication: true)
            };
            return Task.FromResult<IReadOnlyList<ISearchResult>>((Reverse ? entries.Reverse() : entries).ToList());
        }
    }

    [TestMethod]
    public void ToUiResult_MapsAProvidersOwnResultType()
    {
        // Deliberately not an AppSearchResult: a provider lives in a plugin assembly that cannot see
        // that type, so this is the only shape entries ever arrive in.
        var mapped = PluginResultMapper.ToUiResult(new PluginItem(@"C:\Projects\readme.txt"), index: 3);

        Assert.AreEqual("readme.txt", mapped.Name);
        Assert.AreEqual(@"C:\Projects\readme.txt", mapped.FullPath);
        Assert.AreEqual(@"C:\Projects", mapped.ParentDir);
        Assert.AreEqual("C:", mapped.Drive);
        Assert.AreEqual("File", mapped.ResultKind);
        Assert.AreEqual(3, mapped.Index);
    }

    [TestMethod]
    public void ToUiResult_WebAddress_KeepsTheAddressWholeInsteadOfTreatingItAsAPath()
    {
        var mapped = PluginResultMapper.ToUiResult(new PluginItem("https://www.google.com", "Google"), index: 0);

        // Path.GetDirectoryName would have made this "https:".
        Assert.AreEqual("https://www.google.com", mapped.ParentDir);
        Assert.IsNotNull(mapped.IconOverride);
    }

    [TestMethod]
    public void ToUiResult_AnApplication_LaunchesRatherThanFallingThroughToTheFileHandler()
    {
        var mapped = PluginResultMapper.ToUiResult(
            new PluginItem(@"C:\Start Menu\MyApp.lnk", isApplication: true), index: 0);

        Assert.AreEqual("Application", mapped.ResultKind);
        Assert.IsNotNull(mapped.InstantResultOnExecute);
        Assert.AreEqual(@"C:\Start Menu\MyApp.lnk", mapped.InstantResultActionArgument);
    }

    // The panel's default sort is newest-first, and a provider that returns no timestamps (favorites,
    // say) must not have its own order scrambled by it. Pinned because the plugin branch relies on it:
    // it hands over null for every entry a provider left without a Modified time.
    [TestMethod]
    public void Group_EntriesWithNoKnownTime_KeepTheOrderTheProviderReturned()
    {
        var group = new QuickPanelGroupViewModel("plugin::source", "Favorites", string.Empty, new()
        {
            (Row("zebra"), null),
            (Row("apple"), null),
            (Row("mango"), null),
        });

        CollectionAssert.AreEqual(
            new[] { "zebra", "apple", "mango" },
            group.Items.Select(item => item.Name).ToList());
    }

    [TestMethod]
    public void Group_EntriesWithTimes_LeadAndSortNewestFirst()
    {
        var group = new QuickPanelGroupViewModel("plugin::source", "Recent", string.Empty, new()
        {
            (Row("undated"), null),
            (Row("older"), new DateTime(2026, 1, 1)),
            (Row("newer"), new DateTime(2026, 7, 1)),
        });

        CollectionAssert.AreEqual(
            new[] { "newer", "older", "undated" },
            group.Items.Select(item => item.Name).ToList());
    }

    private static AppSearchResult Row(string name) => new() { Name = name, FullPath = @"C:\x\" + name };

    private sealed class PluginItem : ISearchResult
    {
        public PluginItem(string path, string? displayName = null, bool isApplication = false)
        {
            FullPath = path;
            IsApplication = isApplication;
            Name = displayName ?? System.IO.Path.GetFileName(path);
            ContextDirectory = System.IO.Path.GetDirectoryName(path) ?? path;
        }

        public string Name { get; }
        public string FullPath { get; }
        public string ContextDirectory { get; }
        public bool IsDir => false;
        public bool IsApplication { get; }
    }
}
