using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory;

public sealed class ClipboardHistoryPlugin : IPlugin, IConfigurable, IActionProvider, IDisposable
{
    public ClipboardHistoryPlugin() => ClipboardRuntime.Instance.Start();

    public string Name => TranslationService.Get("ClipboardHistory_PluginName");
    public string Description => TranslationService.Get("ClipboardHistory_PluginDesc");
    public PluginConfigSchema GetConfigSchema() => ClipboardSettings.CreateSchema();
    public IEnumerable<ISearchResultAction> GetActions() => [];
    public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => [new ClipboardActionProvider()];
    public Task PrepareForSettingsTransferAsync() => ClipboardRuntime.Instance.StopAsync();
    public void Dispose() => ClipboardRuntime.Instance.Dispose();
}
