using System.Text.Json;
using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine;
using Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.Community;

[TestClass, DoNotParallelize]
public sealed class FlowPluginRecoveryTests
{
    private string _root = null!;
    private FlowPluginHost _host = null!;
    private string Plugins => Path.Combine(_root, "FlowData", "Plugins");

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "FlowRecovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Plugins);
        UserDataService.GetUserDataDirectoryFunc = () => _root;
        FlowPluginStateStore.CustomFilePath = Path.Combine(_root, "states.json");
        _host = new FlowPluginHost(new FlowSettingsStorage(Path.Combine(_root, "settings")), [Plugins]);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _host.DisposeAsync();
        UserDataService.GetUserDataDirectoryFunc = null;
        FlowPluginStateStore.CustomFilePath = null;
        SearchRefreshService.RefreshMatchingFunc = null;
        FlowPluginInstaller.SafeDeleteDirectory(_root);
    }

    private string Package(string folder, string version = "1.0", bool fail = false, string minimum = "")
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var dll = typeof(FlowPluginFixture.Fixture).Assembly.Location;
        File.Copy(dll, Path.Combine(dir, Path.GetFileName(dll)));
        File.WriteAllText(Path.Combine(dir, "plugin.json"), JsonSerializer.Serialize(new PluginMetadata
        {
            ID = "fixture-id", Name = "Fixture", ExecuteFileName = Path.GetFileName(dll),
            Language = "csharp", Version = version, ActionKeyword = "fixture", MinimumAppVersion = minimum
        }));
        if (fail) File.WriteAllText(Path.Combine(dir, "fail-init"), "yes");
        return dir;
    }

    private Task<bool> Load(string dir) => _host.LoadPluginFromDirectoryAsync(dir, Path.Combine(dir, "plugin.json"));
    private static FlowCommunityPlugin Selection(string id = "fixture-id") => new() { ID = id, Name = "Fixture", Language = "csharp" };

    [TestMethod]
    public async Task UnknownUninstall_CannotDeleteAnotherDirectoryInsidePluginRoot()
    {
        var dir = Package(Path.Combine("FlowData", "Plugins", "Fixture"));
        Assert.IsFalse(await FlowPluginInstaller.UninstallPluginAsync(new PluginMetadata { ID = "unknown", Name = "Fixture" }, _host));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "plugin.json")));
    }

    [TestMethod]
    public async Task LinkedPluginDirectory_IsNotLoadedOrDeleted()
    {
        var outside = Package("outside-link");
        var link = Path.Combine(Plugins, "linked");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            FlowPluginInstaller.SafeDeleteDirectory(link);
            await _host.InitializeAsync();
            Assert.IsEmpty(_host.GetAllPlugins());
            Assert.IsTrue(File.Exists(Path.Combine(outside, "plugin.json")));
            Assert.IsFalse(File.Exists(Path.Combine(outside, ".deleted")));
        }
        finally { Directory.Delete(link); }
    }

    [TestMethod]
    public async Task MalformedManifest_IsRejectedWithoutEscapingTheLoader()
    {
        var dir = Package("malformed");
        File.WriteAllText(Path.Combine(dir, "plugin.json"), "{broken");
        Assert.IsFalse(await Load(dir));
        Assert.IsEmpty(_host.GetAllPlugins());
    }

    [TestMethod]
    public async Task FailedPlugin_CanBeRetriedOrUninstalledWithoutLeavingAStaleError()
    {
        var dir = Package(Path.Combine("FlowData", "Plugins", "failed"), fail: true);
        Assert.IsFalse(await Load(dir));
        var failed = _host.FailedPlugins["fixture-id"].Metadata;
        File.Delete(Path.Combine(dir, "fail-init"));
        Assert.IsTrue(await FlowPluginInstaller.RetryPluginAsync(failed, _host));
        Assert.IsEmpty(_host.FailedPlugins);
        Assert.IsTrue(await FlowPluginInstaller.UninstallPluginAsync(_host.GetAllPlugins().Single().Metadata, _host));
        Assert.IsEmpty(_host.GetAllPlugins());
        var another = Package(Path.Combine("FlowData", "Plugins", "failed-again"), fail: true);
        Assert.IsFalse(await Load(another));
        Assert.IsTrue(await FlowPluginInstaller.UninstallPluginAsync(_host.FailedPlugins["fixture-id"].Metadata, _host));
        Assert.IsEmpty(_host.FailedPlugins);
    }

    [TestMethod]
    public async Task InitializingPlugin_DoesNotReceiveQueriesBeforeReady()
    {
        var dir = Package("waiting");
        var signal = Path.Combine(dir, "wait-init");
        File.WriteAllText(signal, "wait");
        var loading = Load(dir);
        try
        {
            for (var attempt = 0; !File.Exists(Path.Combine(dir, "initialized")); attempt++)
            {
                if (attempt > 500) Assert.Fail("Fixture did not enter initialization.");
                await Task.Delay(10);
            }
            Assert.IsFalse(loading.IsCompleted);
            Assert.IsFalse(await Load(dir), "A second load must not replace or unload the initializing plugin.");
            Assert.IsEmpty(await new FlowQueryDispatcher(_host).DispatchQueryAsync("during-init test"));
            Assert.IsEmpty(_host.GetAllPlugins());
            Assert.AreEqual("False", File.ReadAllText(Path.Combine(dir, "init-disabled-state")), "Readiness must not change the user's enablement setting.");
        }
        finally { File.Delete(signal); }
        Assert.IsTrue(await loading.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(_host.IsPluginEnabled("fixture-id"));
        Assert.HasCount(1, await new FlowQueryDispatcher(_host).DispatchQueryAsync("fixture test"));
    }

    [TestMethod]
    public async Task FailedInitialization_DisposesAndRemovesKeywords_AndCanRetry()
    {
        var dir = Package("fixture", fail: true);
        Assert.IsFalse(await Load(dir));
        Assert.IsEmpty(_host.GetAllPlugins());
        Assert.IsFalse(_host.ActionKeywordAssigned("fixture"));
        Assert.IsFalse(_host.ActionKeywordAssigned("during-init"));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "disposed")));
        Assert.Contains("fixture initialization failed", _host.FailedPlugins["fixture-id"].Reason);
        File.Delete(Path.Combine(dir, "fail-init"));
        Assert.IsTrue(await Load(dir));
        Assert.IsFalse(_host.FailedPlugins.ContainsKey("fixture-id"));
        Assert.IsTrue(_host.ActionKeywordAssigned("fixture"));
    }

    [TestMethod]
    public async Task FailedUpdate_RestoresOldPluginAndItsFiles()
    {
        var old = Package(Path.Combine("FlowData", "Plugins", "Fixture"));
        Assert.IsTrue(await Load(old));
        var replacement = Package("replacement", "2.0", fail: true);
        Assert.IsFalse(await FlowPluginInstaller.InstallExtractedPluginAsync(Selection(), replacement, _host));
        Assert.AreEqual("1.0", _host.GetAllPlugins().Single().Metadata.Version);
        Assert.AreEqual(old, _host.GetAllPlugins().Single().Metadata.PluginDirectory);
        Assert.IsTrue(File.Exists(Path.Combine(old, "plugin.json")));
        Assert.IsFalse(File.Exists(Path.Combine(old, ".deleted")));
        Assert.IsTrue(_host.ActionKeywordAssigned("fixture"));
        Assert.HasCount(1, await new FlowQueryDispatcher(_host).DispatchQueryAsync("fixture test"));
    }

    [TestMethod]
    public async Task SuccessfulUpdate_LoadsNewVersion_AndSurvivesRestart()
    {
        var old = Package(Path.Combine("FlowData", "Plugins", "Fixture"));
        Assert.IsTrue(await Load(old));
        Assert.IsTrue(await FlowPluginInstaller.InstallExtractedPluginAsync(Selection(), Package("replacement", "2.0"), _host));
        var installed = _host.GetAllPlugins().Single().Metadata.PluginDirectory;
        Assert.AreEqual("2.0", _host.GetAllPlugins().Single().Metadata.Version);
        Assert.IsFalse(File.Exists(Path.Combine(installed, ".pending")));
        await _host.DisposeAsync();
        _host = new FlowPluginHost(new FlowSettingsStorage(Path.Combine(_root, "settings")), [Plugins]);
        await _host.InitializeAsync();
        Assert.AreEqual("2.0", _host.GetAllPlugins().Single().Metadata.Version);
    }

    [TestMethod]
    public async Task MismatchedPackageIdentity_LeavesInstalledPluginRunning()
    {
        var old = Package(Path.Combine("FlowData", "Plugins", "Fixture"));
        Assert.IsTrue(await Load(old));
        Assert.IsFalse(await FlowPluginInstaller.InstallExtractedPluginAsync(Selection("other-id"), Package("replacement"), _host));
        Assert.IsFalse(File.Exists(Path.Combine(old, "disposed")));
        Assert.AreEqual(old, _host.GetAllPlugins().Single().Metadata.PluginDirectory);
    }

    [TestMethod]
    public async Task UnsupportedMinimumVersion_IsBlockedBeforeInitialization()
    {
        var dir = Package("future", minimum: "999.0.0");
        Assert.IsFalse(await Load(dir));
        Assert.IsFalse(File.Exists(Path.Combine(dir, "initialized")));
        Assert.IsEmpty(_host.GetAllPlugins());
    }

    [TestMethod]
    public async Task UninstallOutsidePluginRoot_DoesNotUnloadOrDelete()
    {
        var outside = Package("outside");
        Assert.IsTrue(await Load(outside));
        Assert.IsFalse(await FlowPluginInstaller.UninstallPluginAsync(_host.GetAllPlugins().Single().Metadata, _host));
        Assert.IsTrue(File.Exists(Path.Combine(outside, "plugin.json")));
        Assert.HasCount(1, _host.GetAllPlugins());
    }

    [TestMethod]
    public async Task DuplicateOperations_AreRejectedWhileInstallIsPending()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SearchRefreshService.RefreshMatchingFunc = _ => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); };
        var selection = Selection();
        selection.UrlDownload = "invalid://no-network";
        var first = Task.Run(() => FlowPluginInstaller.DownloadAndInstallPluginAsync(selection, _host));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsTrue(FlowPluginInstaller.IsInstalling(selection.ID));
            Assert.IsFalse(await FlowPluginInstaller.DownloadAndInstallPluginAsync(selection, _host));
            Assert.IsFalse(await FlowPluginInstaller.UninstallPluginAsync(new PluginMetadata { ID = selection.ID }, _host));
        }
        finally { release.Set(); }
        Assert.IsFalse(await first);
        Assert.IsFalse(FlowPluginInstaller.IsInstalling(selection.ID));
    }

    [TestMethod]
    public async Task InterruptedPackage_IsNotLoadedOnStartup()
    {
        var dir = Package(Path.Combine("FlowData", "Plugins", "pending"));
        File.WriteAllText(Path.Combine(dir, ".pending"), "installing");
        await _host.InitializeAsync();
        Assert.IsEmpty(_host.GetAllPlugins());
        Assert.IsFalse(File.Exists(Path.Combine(dir, "initialized")));
    }

    [TestMethod]
    public async Task CancelledUpdate_DoesNotStopOldPlugin()
    {
        var old = Package(Path.Combine("FlowData", "Plugins", "Fixture"));
        Assert.IsTrue(await Load(old));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            FlowPluginInstaller.InstallExtractedPluginAsync(Selection(), Package("replacement"), _host, cancellation.Token));
        Assert.IsFalse(File.Exists(Path.Combine(old, "disposed")));
        Assert.HasCount(1, _host.GetAllPlugins());
    }
}
