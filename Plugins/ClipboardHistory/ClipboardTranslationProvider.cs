using System.Reflection;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory;

public sealed class ClipboardTranslationProvider : ITranslationProvider
{
    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(Assembly.GetExecutingAssembly());
    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName) =>
        TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureName, "Plugin");
}
