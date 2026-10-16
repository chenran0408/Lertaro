using System.Runtime.InteropServices;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginCompatibilityTests
{
    [TestMethod]
    [DataRow("csharp", "1.19.0", true)]
    [DataRow("python_v2", "", true)]
    [DataRow("javascript", "v1.19.0", true)]
    [DataRow("executable", "2.0.0", false)]
    [DataRow("csharp", "999.0.0", false)]
    [DataRow("python", "invalid", false)]
    [DataRow("unknown", "", false)]
    public void MinimumFlowVersion_IsIndependentOfLertaroVersion(string language, string minimum, bool allowed) =>
        Assert.AreEqual(allowed, FlowPluginCompatibility.GetBlockReason(language, minimum) == null);

    [TestMethod]
    public void Recommendation_IsSpecificToTestedVersionAndArchitecture()
    {
        var plugin = new FlowCommunityPlugin { ID = "84ddc2a6d43911e8a8d5f2801f1b9fd1", Version = "1.0.8" };
        Assert.IsTrue(FlowCommunityRecommendations.IsSearchTested(plugin, Architecture.X64));
        Assert.IsFalse(FlowCommunityRecommendations.IsSearchTested(plugin, Architecture.Arm64));
        Assert.IsNotNull(FlowCommunityRecommendations.GetArchiveHash(plugin));
        plugin.Version = "1.0.9";
        Assert.IsFalse(FlowCommunityRecommendations.IsSearchTested(plugin, Architecture.X64));
        Assert.IsNull(FlowCommunityRecommendations.GetArchiveHash(plugin));
        plugin.ID = "unknown";
        Assert.IsFalse(FlowCommunityRecommendations.IsSearchTested(plugin, Architecture.X64));
    }
}
