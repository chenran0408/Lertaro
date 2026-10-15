using System.Text.Json.Nodes;

namespace Lertaro.Plugins.TaskbarAppearance.Tests;

[TestClass]
public sealed class BundledTaskbarEngineTests
{
    [TestMethod]
    [DataRow("../outside.exe")]
    [DataRow("..\\outside.exe")]
    [DataRow(@"C:\outside.exe")]
    public void ManifestCannotEscapeItsRoot(string path)
        => Assert.Throws<InvalidDataException>(() => BundledTaskbarEngine.SafePath(@"D:\Runtime", path));

    [TestMethod]
    public void ManifestKeepsNestedAssetsInsideRuntime()
        => Assert.AreEqual(@"D:\Runtime\Assets\SplashScreen.jpeg", BundledTaskbarEngine.SafePath(@"D:\Runtime", "Assets/SplashScreen.jpeg"));

    [TestMethod]
    public void BundledEngineHidesItsTrayButExternalConfigurationKeepsUserChoice()
    {
        var bundled = JsonNode.Parse(TaskbarConfiguration.Build("{}", new() { Enabled = true, IsBundled = true }))!;
        Assert.IsTrue(bundled["hide_tray"]!.GetValue<bool>());
        var external = JsonNode.Parse(TaskbarConfiguration.Build("{\"hide_tray\":false}", new() { Enabled = true }))!;
        Assert.IsFalse(external["hide_tray"]!.GetValue<bool>());
    }
}
