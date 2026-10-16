using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowPluginPathHelperTests
{
    [TestMethod]
    [DataRow("../outside")]
    [DataRow("..\\outside")]
    [DataRow("C:\\outside")]
    [DataRow(".")]
    [DataRow("..")]
    [DataRow("CON")]
    [DataRow("nul.txt")]
    [DataRow("COM1")]
    [DataRow("name.")]
    [DataRow("name ")]
    [DataRow("name:stream")]
    public void PluginName_CannotEscapeOrAliasWindowsDirectories(string name)
    {
        Assert.Throws<InvalidDataException>(() => FlowPluginPathHelper.GetSettingsDirectory(@"D:\isolated", name));
        Assert.Throws<InvalidDataException>(() => FlowPluginPathHelper.GetCacheDirectory(@"D:\isolated", name));
    }

    [TestMethod]
    public void GetSettingsDirectory_UsesPluginSettingsFolder()
    {
        var path = FlowPluginPathHelper.GetSettingsDirectory(@"C:\Data", "SamplePlugin");

        Assert.AreEqual(Path.Combine(@"C:\Data", "FlowData", "Settings", "Plugins", "SamplePlugin"), path);
    }

    [TestMethod]
    public void GetCacheDirectory_UsesPluginCachesFolder()
    {
        var path = FlowPluginPathHelper.GetCacheDirectory(@"C:\Data", "SamplePlugin");

        Assert.AreEqual(Path.Combine(@"C:\Data", "FlowData", "Caches", "Plugins", "SamplePlugin"), path);
    }
}
