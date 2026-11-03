using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Plugins.ClipboardHistory;

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

    [TestMethod]
    public void MenuIncludesClearHistoryAfterPerEntryActions()
    {
        var entry = ClipboardInstantProviderTests.Entry(42, "text", pinned: true, time: 1);
        var provider = new ClipboardActionProvider();
        var previousLookup = PluginSdk.Services.TranslationService.LookupFunc;
        PluginSdk.Services.TranslationService.LookupFunc = key => key;
        var entriesField = typeof(ClipboardRuntime).GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(entriesField);
        var previousEntries = (ClipboardEntry[])entriesField.GetValue(ClipboardRuntime.Instance)!;
        entriesField.SetValue(ClipboardRuntime.Instance, new[] { entry });
        try
        {
            var items = provider.GetMenuItems([new ClipboardSearchResult(entry)], IntPtr.Zero).ToArray();

            Assert.HasCount(6, items);
            Assert.AreEqual((uint)ClipboardActionProvider.MenuCommand.ClearHistory, items[^1].CommandId);
            Assert.AreEqual("ClipboardHistory_ClearHistory", items[^1].Text);
            Assert.IsNotNull(items[^1].OnExecute);
        }
        finally
        {
            PluginSdk.Services.TranslationService.LookupFunc = previousLookup;
            entriesField.SetValue(ClipboardRuntime.Instance, previousEntries);
        }
    }
}
