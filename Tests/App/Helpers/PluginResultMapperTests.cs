using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class PluginResultMapperTests
{
    [TestMethod]
    public void VirtualTabItemRetainsItsCallbackAndActionIdentity()
    {
        var executed = false;
        var source = new VirtualItem { Title = "Copied text", ActionArgument = "clip:12", Description = "editor",
            ActionType = "None", OnExecuteFunc = () => { executed = true; return false; } };
        var result = PluginResultMapper.ToUiResult(source, 3);
        Assert.IsTrue(result.IsInstantResult);
        Assert.IsFalse(result.CanPreview);
        Assert.AreEqual("clip:12", result.FullPath);
        Assert.AreEqual("clip:12", ((ISearchResult)result).InstantActionArgument);
        Assert.AreEqual("editor", result.ParentDir);
        Assert.AreEqual(3, result.Index);
        Assert.IsNotNull(result.InstantResultOnExecuteFunc);
        Assert.IsFalse(result.InstantResultOnExecuteFunc());
        Assert.IsTrue(executed);
    }

    private sealed class VirtualItem : InstantResultItem, ISearchResult
    {
        public string Name => Title;
        public string FullPath => ActionArgument;
        public string ContextDirectory => "";
        public bool IsDir => false;
        public bool IsApplication => false;
    }
}
