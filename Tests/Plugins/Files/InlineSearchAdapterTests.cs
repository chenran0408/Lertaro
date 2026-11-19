using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Files.Tests;

[TestClass]
[DoNotParallelize]
public sealed class InlineSearchAdapterTests
{
    [TestMethod]
    public void RecognizesOnlyItsHostAndRespectsTheInlineSearchSetting()
    {
        var adapter = new FilesInlineSearchAdapter();
        var original = PluginSettingsService.GetSettingFunc;
        try
        {
            PluginSettingsService.GetSettingFunc = null;
            Assert.IsTrue(adapter.CanHandle(0, "SharedWindowClass", "FILES"));
            Assert.IsFalse(adapter.CanHandle(0, "SharedWindowClass", "explorer"));
            PluginSettingsService.GetSettingFunc = (id, key, fallback) =>
                id == "Lertaro.Plugins.Files" && key == "EnableInlineSearch" ? false : fallback;
            Assert.IsFalse(adapter.CanHandle(0, "SharedWindowClass", "Files"));
            Assert.IsTrue(adapter.CanRecognizeHost(0, "SharedWindowClass", "Files"));
            Assert.IsFalse(adapter.CanTrigger(0, ""));
        }
        finally { PluginSettingsService.GetSettingFunc = original; }
    }
}
