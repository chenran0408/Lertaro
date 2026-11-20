using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public class TranslationManagerTests
{
    [TestMethod]
    [DataRow(null, "en-US", "en-US")]
    [DataRow("en-US", "zh-CN", "en-US,zh-CN")]
    [DataRow("zh-CN", "zh-CN", "en-US,zh-CN")]
    [DataRow("zh-TW", "en-US", "zh-TW,en-US")]
    [DataRow("zh-TW", "zh-CN", "zh-TW,en-US,zh-CN")]
    public void TranslationCultures_LoadEachLanguageOnceInOverrideOrder(string? fallback, string active, string expected)
    {
        CollectionAssert.AreEqual(expected.Split(','), TranslationManager.GetTranslationCultures(fallback, active).ToArray());
    }
}
