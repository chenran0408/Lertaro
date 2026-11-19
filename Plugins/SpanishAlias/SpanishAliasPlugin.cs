using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.SpanishAlias;

public sealed class SpanishAliasPlugin : IPlugin
{
    public string Id => "Lertaro.Plugins.SpanishAlias";
    public string Name => TranslationService.Get("Plugins_SpanishAliasPluginName");
    public string Description => TranslationService.Get("Plugin_Comp_Desc_SpanishAliasProvider");
    public string Version => "1.0.0";
    public string Author => "Lertaro";
}
