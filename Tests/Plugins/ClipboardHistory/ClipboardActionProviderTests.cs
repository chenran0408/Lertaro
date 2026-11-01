using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardActionProviderTests
{
    [TestMethod]
    public void OnlyRecognizesOneClipboardResultWithPositiveNumericIdentity()
    {
        var result = new ClipboardSearchResult(ClipboardInstantProviderTests.Entry(42, "text", false, 1));
        Assert.IsTrue(ClipboardActionProvider.TryGetId([result], out var id));
        Assert.AreEqual(42L, id);
        Assert.IsFalse(ClipboardActionProvider.TryGetId([], out _));
        Assert.IsFalse(ClipboardActionProvider.TryGetId([result, result], out _));
        foreach (var invalid in new[] { "clip:0", "clip:-1", "clip: 1", "clip:1.exe", "clip:9223372036854775808", "file:42" })
        {
            result.ActionArgument = invalid;
            Assert.IsFalse(ClipboardActionProvider.TryGetId([result], out _), invalid);
        }
    }

    [TestMethod]
    public void TabResultKeepsStableIdentityAndTimestampWithoutFileSemantics()
    {
        ISearchResult result = new ClipboardSearchResult(ClipboardInstantProviderTests.Entry(4, "text", true, 1000));
        Assert.AreEqual("clip:4", result.FullPath);
        Assert.AreEqual(result.FullPath, result.InstantActionArgument);
        Assert.IsFalse(result.IsDir);
        Assert.IsFalse(result.IsApplication);
        Assert.IsInstanceOfType<InstantResultItem>(result);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1000).LocalDateTime, result.Metadata.Modified);
    }
}
