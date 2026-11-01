namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class ClipboardTranslationProviderTests
{
    [TestMethod]
    public void ShipsSevenCompleteLocalesWithEverySettingsLabelAndAction()
    {
        var provider = new ClipboardTranslationProvider();
        CollectionAssert.AreEquivalent(new[] { "en-US", "zh-CN", "zh-HK", "zh-TW", "ja-JP", "ko-KR", "es-ES" },
            provider.SupportedCultures.ToArray());
        var expectedKeys = provider.GetTranslations("zh-CN").Keys.ToArray();
        foreach (var culture in provider.SupportedCultures)
        {
            var translations = provider.GetTranslations(culture);
            CollectionAssert.AreEquivalent(expectedKeys, translations.Keys.ToArray(), culture);
            Assert.IsTrue(translations.Values.All(value => !string.IsNullOrWhiteSpace(value)), culture);
            foreach (var field in ClipboardSettings.CreateSchema().Fields)
            {
                Assert.IsTrue(translations.ContainsKey(field.LabelKey), field.LabelKey);
                Assert.IsTrue(translations.ContainsKey(field.DescriptionKey), field.DescriptionKey);
            }
            foreach (var action in new[] { "Paste", "Copy", "Pin", "Unpin", "Delete", "OpenSource" })
                Assert.IsTrue(translations.ContainsKey("ClipboardHistory_" + action), culture + ":" + action);
        }
    }
}
