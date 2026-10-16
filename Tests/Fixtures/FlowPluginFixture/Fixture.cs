using Flow.Launcher.Plugin;

namespace FlowPluginFixture;

// An actual collectible plugin assembly: exercises the production loader and cleanup paths.
public sealed class Fixture : IAsyncPlugin, IDisposable
{
    private PluginMetadata? _metadata;
    public async Task InitAsync(PluginInitContext context)
    {
        _metadata = context.CurrentPluginMetadata;
        File.WriteAllText(Path.Combine(_metadata.PluginDirectory, "init-disabled-state"), _metadata.Disabled.ToString());
        context.API.AddActionKeyword(_metadata.ID, "during-init");
        File.WriteAllText(Path.Combine(_metadata.PluginDirectory, "initialized"), "yes");
        for (var attempt = 0; File.Exists(Path.Combine(_metadata.PluginDirectory, "wait-init")); attempt++)
        {
            if (attempt > 500) throw new TimeoutException("fixture initialization was not released");
            await Task.Delay(10);
        }
        if (File.Exists(Path.Combine(_metadata.PluginDirectory, "fail-init")))
            throw new InvalidOperationException("fixture initialization failed");
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token) =>
        Task.FromResult(new List<Result> { new() { Title = _metadata!.Version } });

    public void Dispose()
    {
        if (_metadata != null) File.WriteAllText(Path.Combine(_metadata.PluginDirectory, "disposed"), "yes");
    }
}
