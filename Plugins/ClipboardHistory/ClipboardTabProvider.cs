using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ClipboardHistory;

public sealed class ClipboardTabProvider : IQuickPanelTabProvider
{
    public string Name => TranslationService.Get("ClipboardHistory_TabName");

    /// <summary>Opens the dedicated panel. The host's Win+V wake path calls this through reflection
    /// (it cannot reference this assembly); a false return sends that path to the Quick Panel tab
    /// fallback, which happens when there is no UI thread to host the window.</summary>
    public static bool TryOpenPanel() => View.ClipboardPanelView.TryShow();

    /// <summary>Host-called global quick paste (Ctrl+Win+N): pastes the Nth entry of the panel's
    /// canonical order straight into the active window. Resolved by name; returns whether a paste
    /// was started.</summary>
    public static bool TryQuickPaste(int oneBased) => ClipboardRuntime.Instance.PasteSlot(oneBased);

    public async Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ClipboardSettings.Enabled) return [];
        var entries = await ClipboardRuntime.Instance.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return entries.Select(entry => (ISearchResult)new ClipboardSearchResult(entry)).ToArray();
    }
}

// One existing SDK shape for execution and one for tab rendering, with no filesystem stand-in.
internal sealed class ClipboardSearchResult : InstantResultItem, ISearchResult
{
    internal ClipboardSearchResult(ClipboardEntry entry)
    {
        Title = entry.PlainPreview;
        Description = (entry.Pinned ? "\u2605 " : string.Empty) + entry.SourceProcess;
        ActionType = "None";
        ActionArgument = $"clip:{entry.Id}";
        IconData = ClipboardInstantProvider.ClipboardIcon;
        IconColor = "AccentBlue";
        OnExecuteFunc = () => ClipboardRuntime.Instance.PasteAndClose(entry.Id);
        Metadata = new FileMetadata(0, DateTimeOffset.FromUnixTimeMilliseconds(entry.CreatedAtMs).LocalDateTime,
            DateTimeOffset.FromUnixTimeMilliseconds(entry.LastSeenAtMs).LocalDateTime, DateTime.MinValue);
    }

    public string Name => Title;
    public string FullPath => ActionArgument;
    public string ContextDirectory => string.Empty;
    public bool IsDir => false;
    public bool IsApplication => false;
    public FileMetadata Metadata { get; }
    public string? InstantActionArgument => ActionArgument;
}
