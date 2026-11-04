using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class UpdateArtifactCleanupTests
{
    private string _installDir = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _installDir = Path.Combine(Path.GetTempPath(), "LertaroUpdateCleanupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_installDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_installDir, recursive: true); } catch { }
    }

    [TestMethod]
    public void CleanInstallDirectory_RemovesPayloadVerdictAndLock()
    {
        var payload = Path.Combine(_installDir, UpdateApplyRequestHandler.PayloadStagingFolderName, "rollback-abc");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "Lertaro.App.exe"), "old");
        File.WriteAllText(Path.Combine(_installDir, "update-result.json"), "{}");
        File.WriteAllText(Path.Combine(_installDir, "update.lock"), "");

        UpdateArtifactCleanup.CleanInstallDirectory(_installDir);

        Assert.IsFalse(Directory.Exists(Path.Combine(_installDir, UpdateApplyRequestHandler.PayloadStagingFolderName)));
        Assert.IsFalse(File.Exists(Path.Combine(_installDir, "update-result.json")));
        Assert.IsFalse(File.Exists(Path.Combine(_installDir, "update.lock")));
    }

    [TestMethod]
    public void CleanInstallDirectory_NoLeftovers_IsANoOp()
    {
        File.WriteAllText(Path.Combine(_installDir, "Lertaro.App.exe"), "current build");

        UpdateArtifactCleanup.CleanInstallDirectory(_installDir);

        Assert.IsTrue(File.Exists(Path.Combine(_installDir, "Lertaro.App.exe")));
    }

    [TestMethod]
    public void CleanInstallDirectory_KeepsEverythingElse()
    {
        var userFile = Path.Combine(_installDir, "user-settings.json");
        File.WriteAllText(userFile, "{}");

        UpdateArtifactCleanup.CleanInstallDirectory(_installDir);

        Assert.IsTrue(File.Exists(userFile));
    }
}
