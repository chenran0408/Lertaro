using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.TaskbarAppearance.Tests;

[TestClass, DoNotParallelize]
public sealed class TaskbarAppearancePluginTests
{
    private string _directory = "";

    [TestInitialize]
    public async Task Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "taskbar-preview-" + Guid.NewGuid().ToString("N"));
        UserDataService.GetUserDataDirectoryFunc = () => _directory;
        PluginSettingsService.GetSettingFunc = null;
        await TaskbarEngine.ApplyAsync(new() { Enabled = false });
    }

    [TestCleanup]
    public void Cleanup()
    {
        UserDataService.GetUserDataDirectoryFunc = null;
        PluginSettingsService.GetSettingFunc = null;
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task ExternalPreviewIsRejectedBeforeTouchingAnExternalConfiguration()
    {
        var schema = new TaskbarAppearancePlugin().GetConfigSchema();
        Assert.IsNotNull(schema.OnSaveAsync);
        Assert.IsNotNull(schema.OnPreviewAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => schema.OnPreviewAsync(new Dictionary<string, object?>
        {
            ["Enabled"] = true, ["ExecutablePath"] = @"D:\other\TranslucentTB.exe"
        }));
        schema.OnRollback?.Invoke();
        Assert.IsFalse(Directory.Exists(_directory));
    }

    [TestMethod]
    public async Task InvalidSettingsAreRejectedBeforePreparingTheBundle()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TaskbarEngine.ApplyAsync(new() { Enabled = true, Color = "invalid" }));
        Assert.IsFalse(Directory.Exists(_directory));
    }

    [TestMethod]
    public async Task DisabledPreviewNeedsNoBundleAndWritesNoPreferences()
    {
        var schema = new TaskbarAppearancePlugin().GetConfigSchema();
        await schema.OnPreviewAsync!(new Dictionary<string, object?> { ["Enabled"] = false });
        schema.OnRollback?.Invoke();
        Assert.IsFalse(Directory.Exists(_directory));
    }

    [TestMethod]
    public async Task StartupResumeUsesCurrentPreferencesAndCannotRestartAfterTransferSuspendsIt()
    {
        await TaskbarEngine.ApplyAsync(new() { Enabled = false });
        await TaskbarEngine.ResumeAsync(() => new() { Enabled = false });
        await TaskbarEngine.SuspendAsync();
        var read = false;
        await TaskbarEngine.ResumeAsync(() => { read = true; return new() { Enabled = true }; });
        Assert.IsFalse(read);
        Assert.IsFalse(Directory.Exists(_directory));
        await TaskbarEngine.ApplyAsync(new() { Enabled = false });
    }
}
