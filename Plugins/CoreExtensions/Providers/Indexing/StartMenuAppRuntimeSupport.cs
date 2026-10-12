using System.IO;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.Indexing;

/// <summary>
/// Owns the Start Menu provider's directory watcher lifecycle so the provider remains below the
/// repository per-file line limit. This support object always operates on one provider instance.
/// </summary>
internal sealed class StartMenuAppRuntimeSupport : IDisposable
{
    private const string RegistrationId = "CoreExtensions.StartMenu";
    private readonly StartMenuAppItemProvider _owner;
    private readonly object _lock = new();
    private IDisposable? _directoryWatch;
    private bool _disposed;
    private System.Threading.Timer? _registryTimer;
    private string? _registeredFingerprint;
    private int _registryReadRunning;
    private int _directoryGeneration;

    public StartMenuAppRuntimeSupport(StartMenuAppItemProvider owner)
    {
        _owner = owner;
        PluginSettingsService.ComponentEnablementChanged += UpdateRuntimeState;
        UpdateRuntimeState();
    }

    public bool IsComponentEnabled => PluginSettingsService.IsComponentEnabled(
        Path.GetFileName(typeof(StartMenuAppItemProvider).Assembly.Location),
        "SearchableItemProvider", nameof(StartMenuAppItemProvider));

    private void UpdateRuntimeState()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            if (IsComponentEnabled)
            {
                if (_directoryWatch != null)
                    return;

                _directoryWatch = DirectoryIndexerService.WatchDirectories(
                    RegistrationId, _owner.NotifyItemsChanged);
                RequestDirectoryRefresh();
                _registryTimer = new System.Threading.Timer(_ => RefreshRegisteredApplications(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                return;
            }

            _registryTimer?.Dispose();
            _registryTimer = null;
            _directoryWatch?.Dispose();
            _directoryWatch = null;
            DirectoryIndexerService.UnregisterDirectories(RegistrationId);
        }
    }

    internal List<RegisteredApplication> ReadRegisteredApplications()
    {
        var entries = RegisteredApplicationDiscovery.Read();
        var fingerprint = RegisteredApplicationDiscovery.RuntimeFingerprint(entries);
        lock (_lock) _registeredFingerprint = fingerprint;
        return entries;
    }

    internal void RequestDirectoryRefresh()
    {
        var generation = Interlocked.Increment(ref _directoryGeneration);
        Task.Run(() =>
        {
            try
            {
                var roots = StartMenuAppItemProvider.GetRoots();
                lock (_lock)
                {
                    if (_disposed || generation != _directoryGeneration || _directoryWatch == null) return;
                    _owner.RefreshDirectoryRegistrations(roots);
                }
                _owner.NotifyItemsChanged();
            }
            catch (Exception ex) { PluginSdk.Logger.Log($"[ApplicationCatalog] Folder refresh failed: {ex.Message}", PluginSdk.LogLevel.Warn); }
        });
    }

    internal void ObserveRegisteredApplications(string fingerprint)
    {
        lock (_lock)
        {
            if (_disposed || _registryTimer == null || _registeredFingerprint == fingerprint) return;
            _registeredFingerprint = fingerprint;
        }
        _owner.NotifyItemsChanged();
    }

    private void RefreshRegisteredApplications()
    {
        if (Interlocked.Exchange(ref _registryReadRunning, 1) != 0) return;
        try
        {
            ObserveRegisteredApplications(RegisteredApplicationDiscovery.RuntimeFingerprint(RegisteredApplicationDiscovery.Read()));
        }
        catch (Exception ex) { PluginSdk.Logger.Log($"[ApplicationCatalog] Registry refresh failed: {ex.Message}", PluginSdk.LogLevel.Warn); }
        finally { Volatile.Write(ref _registryReadRunning, 0); }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _registryTimer?.Dispose();
            _registryTimer = null;
            _directoryWatch?.Dispose();
            _directoryWatch = null;
            PluginSettingsService.ComponentEnablementChanged -= UpdateRuntimeState;
            try
            {
                DirectoryIndexerService.UnregisterDirectories(RegistrationId);
            }
            catch { }
        }
    }
}
