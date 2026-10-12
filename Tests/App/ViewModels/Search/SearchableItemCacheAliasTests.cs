using Lertaro.App.ViewModels.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
[DoNotParallelize]
public sealed class SearchableItemCacheAliasTests
{
    private const string ProviderId = nameof(AliasCatalogProvider);

    [TestInitialize]
    public void Initialize() => Reset();

    [TestCleanup]
    public void Cleanup() => Reset();

    private static void Reset()
    {
        SearchableItemCache.Seed(ProviderId, []);
        SearchableItemCache.Invalidate(ProviderId);
    }

    [TestMethod]
    public async Task ApplicationAliasesAreGeneratedDuringCatalogLoadingAndCanBeSearched()
    {
        var entries = await Load(new AliasCatalogProvider());
        Assert.IsTrue(entries[0].Aliases.Any(alias => alias.Equals("vscode", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(FuzzyQuery.Parse("vscode").BestMatch(entries[0].Item.Title, entries[0].Aliases).IsMatch);
    }

    [TestMethod]
    public async Task ProviderCanOptOutOfGeneratedApplicationAliases()
    {
        var entries = await Load(new AliasCatalogProvider { EnableAlias = false });
        Assert.HasCount(0, entries[0].Aliases);
    }

    [TestMethod]
    public async Task OrdinaryFilesDoNotReceiveApplicationAbbreviations()
    {
        var entries = await Load(new AliasCatalogProvider { ResultKind = "File" });
        Assert.IsFalse(entries[0].Aliases.Any(alias => alias.Equals("vscode", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<List<SearchableItemCache.CacheEntry>> Load(AliasCatalogProvider provider)
    {
        SearchableItemCache.EnsureLoaded(provider);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            if (SearchableItemCache.TryGetEntries(ProviderId, out var entries) && entries.Count > 0)
                return entries;
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class AliasCatalogProvider : ISearchableItemProvider
    {
        public string Name => "Alias catalog fixture";
        public event Action? ItemsChanged { add { } remove { } }
        public bool EnableAlias { get; init; } = true;
        public string ResultKind { get; init; } = "Application";
        public IEnumerable<SearchableItem> GetSearchableItems() =>
            [new() { Title = "Visual Studio Code", ResultKind = ResultKind, ActionArgument = @"C:\Apps\Code.exe" }];
    }
}
