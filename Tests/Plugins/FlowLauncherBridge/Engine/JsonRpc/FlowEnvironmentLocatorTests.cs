using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
[DoNotParallelize]
public sealed class FlowEnvironmentLocatorTests
{
    private string? _tempUser;
    private string? _tempShared;

    [TestInitialize]
    public void Setup()
    {
        FlowEnvironmentLocator.ResetCache();
        _tempUser = Path.Combine(Path.GetTempPath(), "LertaroTestUser_" + Guid.NewGuid().ToString("N"));
        _tempShared = Path.Combine(Path.GetTempPath(), "LertaroTestShared_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempUser);

        UserDataService.GetUserDataDirectoryFunc = () => _tempUser;
        UserDataService.GetSharedDataDirectoryFunc = () => _tempShared;
    }

    [TestCleanup]
    public void Cleanup()
    {
        FlowEnvironmentLocator.ResetCache();
        UserDataService.GetUserDataDirectoryFunc = null;
        UserDataService.GetSharedDataDirectoryFunc = null;

        try { if (_tempUser != null && Directory.Exists(_tempUser)) Directory.Delete(_tempUser, true); } catch { }
    }

    [TestMethod]
    public void GetEmbeddedPythonDirectory_ResolvesUnderUserDataDirectory()
    {
        // Not the machine-wide directory: a runtime there could be seeded by another user.
        var dir = FlowEnvironmentLocator.GetEmbeddedPythonDirectory();
        Assert.AreEqual(Path.Combine(_tempUser!, "FlowData"), Path.GetDirectoryName(dir));
        Assert.Contains("PythonEmbeded-", dir);
        Assert.IsFalse(dir.StartsWith(_tempShared!, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GetEmbeddedNodeDirectory_ResolvesUnderUserDataDirectory()
    {
        var dir = FlowEnvironmentLocator.GetEmbeddedNodeDirectory();
        Assert.AreEqual(Path.Combine(_tempUser!, "FlowData"), Path.GetDirectoryName(dir));
        Assert.Contains("NodeEmbeded-", dir);
        Assert.IsFalse(dir.StartsWith(_tempShared!, StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RuntimeCache_DoesNotFollowThePreviousUserDataDirectory()
    {
        var oldNodeDir = FlowEnvironmentLocator.GetEmbeddedNodeDirectory();
        var oldPythonDir = FlowEnvironmentLocator.GetEmbeddedPythonDirectory();
        Directory.CreateDirectory(oldNodeDir);
        Directory.CreateDirectory(oldPythonDir);
        File.WriteAllText(Path.Combine(oldNodeDir, "node.exe"), "dummy");
        File.WriteAllText(Path.Combine(oldPythonDir, "python.exe"), "dummy");
        Assert.IsNotNull(FlowEnvironmentLocator.FindNodeExecutable());
        Assert.IsNotNull(FlowEnvironmentLocator.FindPythonExecutable());
        UserDataService.GetUserDataDirectoryFunc = () => Path.Combine(_tempUser!, "new-data");
        Assert.IsNull(FlowEnvironmentLocator.FindNodeExecutable());
        Assert.IsNull(FlowEnvironmentLocator.FindPythonExecutable());
    }

    [TestMethod]
    public void FindPythonExecutable_WhenInUserDirectory_ReturnsThatPath()
    {
        var embed = FlowEnvironmentLocator.GetEmbeddedPythonDirectory();
        Directory.CreateDirectory(embed);
        var dummyExe = Path.Combine(embed, "python.exe");
        File.WriteAllText(dummyExe, "dummy");

        var found = FlowEnvironmentLocator.FindPythonExecutable();
        Assert.AreEqual(dummyExe, found);
    }

    [TestMethod]
    public void FindNodeExecutable_WhenInUserDirectory_ReturnsThatPath()
    {
        var embed = FlowEnvironmentLocator.GetEmbeddedNodeDirectory();
        Directory.CreateDirectory(embed);
        var dummyExe = Path.Combine(embed, "node.exe");
        File.WriteAllText(dummyExe, "dummy");

        var found = FlowEnvironmentLocator.FindNodeExecutable();
        Assert.AreEqual(dummyExe, found);
    }
}
