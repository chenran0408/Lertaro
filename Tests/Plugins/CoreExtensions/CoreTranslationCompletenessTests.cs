using Lertaro.Plugins.CoreExtensions.Providers.Theming;

namespace Lertaro.Plugins.CoreExtensions.Tests;

/// <summary>
/// W1 i18n completeness gate: every key that exists in ANY locale of the Core provider (the host's
/// App.json + Plugin.json translation files) must exist with a non-empty value in ALL seven
/// supported locales. This is the global complement to the per-plugin
/// TranslationProviderTests -- those cover plugin-owned keys, this one covers the host's own.
/// Failures list the exact key × locale gaps so a fix is mechanical.
/// </summary>
[TestClass]
public sealed class CoreTranslationCompletenessTests
{
    private static readonly string[] Cultures =
        ["en-US", "zh-CN", "zh-HK", "zh-TW", "ja-JP", "ko-KR", "es-ES"];

    [TestMethod]
    public void EveryKeyExistsInAllSevenLocalesWithNonEmptyValues()
    {
        var provider = new CoreTranslationProvider();
        var perLocale = Cultures.ToDictionary(
            culture => culture,
            culture => provider.GetTranslations(culture),
            StringComparer.OrdinalIgnoreCase);

        // The reference set is the union of all locales: a key that exists in one language but not
        // another is exactly the gap this test hunts.
        var allKeys = perLocale.Values
            .SelectMany(dict => dict.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.IsGreaterThan(200, allKeys.Count, "the host translation set looks truncated");

        var gaps = new List<string>();
        foreach (var key in allKeys)
        {
            foreach (var culture in Cultures)
            {
                if (!perLocale[culture].TryGetValue(key, out var value) ||
                    string.IsNullOrWhiteSpace(value))
                {
                    gaps.Add($"{key} × {culture}");
                }
            }
        }

        Assert.IsEmpty(gaps,
            "missing or empty translations (key × locale):\n" + string.Join("\n", gaps));
    }
}
