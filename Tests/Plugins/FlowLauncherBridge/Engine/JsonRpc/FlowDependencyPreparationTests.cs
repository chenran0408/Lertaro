using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass, DoNotParallelize]
public sealed class FlowDependencyPreparationTests
{
    private string _root = null!;
    private string _runtime = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FlowDependencies-" + Guid.NewGuid().ToString("N"));
        _runtime = Path.Combine(_root, "runtime", "node.exe");
        Directory.CreateDirectory(Path.Combine(_root, "runtime", "node_modules", "npm", "bin"));
        File.WriteAllText(Path.Combine(_root, "runtime", "node_modules", "npm", "bin", "npm-cli.js"), "fixture");
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowPipManager.ProcessRunnerOverride = null;
        FlowNpmManager.ProcessRunnerOverride = null;
        Directory.Delete(_root, true);
    }

    private string Plugin(string name, bool python)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, python ? "requirements.txt" : "package.json"), python ? "fixture==1" : "{}");
        return dir;
    }

    private Task<bool> Prepare(string dir, bool python) => python
        ? FlowPipManager.EnsurePipAndRequirementsAsync(_runtime, dir)
        : FlowNpmManager.EnsureNpmAndPackagesAsync(_runtime, dir);

    [TestMethod, DataRow(true), DataRow(false)]
    public async Task PreparedPluginWithoutDependencyFile_NeedsNoInstaller(bool python)
    {
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        Assert.IsTrue(await Prepare(Path.Combine(_root, "empty"), python));
    }

    [TestMethod]
    public async Task CompleteBundledNodeDependencies_AreKeptWithoutRunningInstallScripts()
    {
        var plugin = Plugin("bundled", false);
        var bundled = Path.Combine(plugin, "node_modules");
        Directory.CreateDirectory(bundled);
        var sentinel = Path.Combine(bundled, "bundled.txt");
        File.WriteAllText(sentinel, "bundled dependencies");
        FlowNpmManager.ProcessRunnerOverride = (_, arguments, _) =>
        {
            Assert.Contains("ls --omit=dev --depth=0", arguments);
            Assert.IsTrue(File.Exists(sentinel));
            return Task.FromResult(true);
        };
        Assert.IsTrue(await Prepare(plugin, false));
        Assert.IsTrue(File.Exists(sentinel));
        Assert.IsTrue(File.Exists(Path.Combine(plugin, ".npm_installed")));
    }

    [TestMethod, DataRow(true), DataRow(false)]
    public async Task ConcurrentPlugins_AllPrepare_AndDuplicateRequestIsDeduplicated(bool python)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installCount = 0;
        async Task<bool> Run(string exe, string arguments, string? dir)
        {
            if (arguments.Contains("--version")) return true;
            Interlocked.Increment(ref installCount);
            entered.TrySetResult();
            await release.Task;
            return true;
        }
        FlowPipManager.ProcessRunnerOverride = Run;
        FlowNpmManager.ProcessRunnerOverride = (exe, args, dir) => Run(exe, args, dir);
        var firstDir = Plugin("first", python);
        var secondDir = Plugin("second", python);
        var first = Prepare(firstDir, python);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = Prepare(firstDir, python);
        var second = Prepare(secondDir, python);
        Assert.IsFalse(duplicate.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        release.SetResult();
        var results = await Task.WhenAll(first, duplicate, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(results.All(result => result));
        Assert.AreEqual(2, installCount);
    }

    [TestMethod, DataRow(true), DataRow(false)]
    public async Task ChangedRequirements_OrRuntime_PreparesAgain_AndFailureCanRetry(bool python)
    {
        var count = 0;
        var success = false;
        Task<bool> Run(string exe, string arguments, string? dir)
        {
            if (arguments.Contains("--version")) return Task.FromResult(true);
            if (python) Assert.Contains("--upgrade", arguments);
            count++;
            return Task.FromResult(success);
        }
        FlowPipManager.ProcessRunnerOverride = Run;
        FlowNpmManager.ProcessRunnerOverride = (exe, args, dir) => Run(exe, args, dir);
        var plugin = Plugin("plugin", python);
        var marker = Path.Combine(plugin, python ? ".requirements_installed" : ".npm_installed");
        File.WriteAllText(marker, "old-timestamp-marker");
        Assert.IsFalse(await Prepare(plugin, python));
        Assert.IsFalse(File.Exists(marker));
        success = true;
        Assert.IsTrue(await Prepare(plugin, python));
        Assert.IsTrue(await Prepare(plugin, python));
        Assert.AreEqual(2, count);
        File.AppendAllText(Path.Combine(plugin, python ? "requirements.txt" : "package.json"), "\n");
        Assert.IsTrue(await Prepare(plugin, python));
        Assert.AreEqual(3, count);
        if (!python)
        {
            File.WriteAllText(Path.Combine(plugin, "package-lock.json"), "{}");
            Assert.IsTrue(await Prepare(plugin, python));
            Assert.AreEqual(4, count);
        }
        // A runtime upgrade changes the fingerprint even with unchanged dependency files.
        Assert.AreNotEqual(FlowDependencyMarker.Fingerprint(_runtime, Path.Combine(plugin, "requirements.txt")),
            FlowDependencyMarker.Fingerprint(_runtime + "-new", Path.Combine(plugin, "requirements.txt")));
    }
}
