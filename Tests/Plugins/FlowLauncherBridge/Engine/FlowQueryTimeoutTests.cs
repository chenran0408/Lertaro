using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine;

[TestClass]
public sealed class FlowQueryTimeoutTests
{
    private sealed class WaitingPlugin : IAsyncPlugin
    {
        internal readonly TaskCompletionSource<List<Result>> Results = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        internal bool Synchronous;
        internal bool ThrowOnCancel;
        public Task InitAsync(PluginInitContext context) => Task.CompletedTask;
        public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            Started.TrySetResult();
            if (ThrowOnCancel) token.Register(() => throw new InvalidOperationException("plugin cancellation failed"));
            if (Synchronous) return Task.FromResult(Results.Task.GetAwaiter().GetResult());
            return Results.Task; // Deliberately ignores cancellation.
        }
    }

    private static void Register(FlowPluginHost host, string id, WaitingPlugin plugin) => host.RegisterPlugin(new PluginPair
    {
        Metadata = new PluginMetadata { ID = id, Name = id, ActionKeyword = "*" }, Plugin = plugin
    });

    [TestMethod, DataRow(false, false), DataRow(true, false), DataRow(false, true)]
    public async Task HungPlugin_DoesNotHideFastResults_OrAccumulateRunningCalls(bool synchronous, bool throwOnCancel)
    {
        await using var host = new FlowPluginHost(new FlowSettingsStorage(Path.GetTempPath()), []);
        var slow = new WaitingPlugin { Synchronous = synchronous, ThrowOnCancel = throwOnCancel };
        var fast = new WaitingPlugin();
        fast.Results.SetResult([new Result { Title = "fast" }]);
        Register(host, "slow", slow);
        Register(host, "fast", fast);
        var dispatcher = new FlowQueryDispatcher(host, TimeSpan.FromMilliseconds(100));
        try
        {
            var results = await dispatcher.DispatchQueryAsync("first").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual("fast", results.Single().Title);
            var second = await dispatcher.DispatchQueryAsync("second").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual("fast", second.Single().Title);
            Assert.AreEqual(1, slow.Calls, "A stuck plugin must not receive unlimited overlapping calls.");
        }
        finally { slow.Results.TrySetResult([]); }
    }

    [TestMethod]
    public async Task CallerCancellation_ReturnsEvenWhenPluginIgnoresToken()
    {
        await using var host = new FlowPluginHost(new FlowSettingsStorage(Path.GetTempPath()), []);
        var plugin = new WaitingPlugin();
        Register(host, "waiting", plugin);
        var dispatcher = new FlowQueryDispatcher(host, TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var dispatch = dispatcher.DispatchQueryAsync("cancel", cancellation.Token);
        try
        {
            await plugin.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.IsEmpty(await dispatch.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { plugin.Results.TrySetResult([]); }
    }
}
