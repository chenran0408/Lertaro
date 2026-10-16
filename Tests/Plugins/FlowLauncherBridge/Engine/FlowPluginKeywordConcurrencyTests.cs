using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginKeywordConcurrencyTests
{
    [TestMethod]
    public void PublishedKeywords_AreSnapshots_AndDisabledPluginsStayDisabled()
    {
        var manager = new FlowPluginKeywordManager();
        var pair = new PluginPair { Metadata = new PluginMetadata { ID = "snapshot", ActionKeyword = "old" } };
        manager.RegisterPluginKeywords(pair);
        var before = manager.KeywordPlugins;
        manager.UpdateActionKeyword(pair, "new");
        Assert.HasCount(1, before["old"]);
        Assert.IsFalse(manager.ActionKeywordAssigned("old"));
        pair.Metadata.Disabled = true;
        manager.UpdateActionKeyword(pair, "disabled");
        Assert.IsFalse(manager.ActionKeywordAssigned("disabled"));
        Assert.IsEmpty(manager.GlobalPlugins);
    }

    [TestMethod]
    public async Task ConcurrentRegistrationAndSearch_DoNotLoseSharedKeywords()
    {
        var manager = new FlowPluginKeywordManager();
        var retained = new PluginPair { Metadata = new PluginMetadata { ID = "retained", ActionKeyword = "shared" } };
        manager.RegisterPluginKeywords(retained);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            var pair = new PluginPair { Metadata = new PluginMetadata { ID = "worker-" + worker, ActionKeyword = "shared" } };
            for (var iteration = 0; iteration < 200; iteration++)
            {
                manager.RegisterPluginKeywords(pair);
                foreach (var entry in manager.KeywordPlugins)
                    foreach (var plugin in entry.Value) Assert.IsNotNull(plugin.Metadata);
                manager.UnregisterPluginKeywords(pair);
            }
        })));
        Assert.AreSame(retained, manager.KeywordPlugins["shared"].Single());
    }
}
