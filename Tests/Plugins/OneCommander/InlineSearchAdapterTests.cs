using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.OneCommander.Tests;

[TestClass]
[DoNotParallelize]
public sealed class InlineSearchAdapterTests
{
    [TestMethod]
    public void RecognizesOnlyItsHostAndRespectsTheInlineSearchSetting()
    {
        var adapter = new OneCommanderInlineSearchAdapter();
        var original = PluginSettingsService.GetSettingFunc;
        try
        {
            PluginSettingsService.GetSettingFunc = null;
            Assert.IsTrue(adapter.CanHandle(0, "SharedWindowClass", "ONECOMMANDER"));
            Assert.IsFalse(adapter.CanHandle(0, "SharedWindowClass", "explorer"));
            PluginSettingsService.GetSettingFunc = (id, key, fallback) =>
                id == "Lertaro.Plugins.OneCommander" && key == "EnableInlineSearch" ? false : fallback;
            Assert.IsFalse(adapter.CanHandle(0, "SharedWindowClass", "OneCommander"));
            Assert.IsTrue(adapter.CanRecognizeHost(0, "SharedWindowClass", "OneCommander"));
            Assert.IsFalse(adapter.CanTrigger(0, ""));
        }
        finally { PluginSettingsService.GetSettingFunc = original; }
    }
}
