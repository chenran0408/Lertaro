using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class UpdateApplyCleanupTests
{
    [TestMethod]
    public void Discard_OnlyRemovesTheUnlaunchedPayload_AndPreservesRollbackAndOtherRuns()
    {
        var install = Directory.CreateTempSubdirectory("LertaroPayloadCleanup_").FullName;
        try
        {
            var staging = Path.Combine(install, UpdateApplyRequestHandler.PayloadStagingFolderName);
            var current = Directory.CreateDirectory(Path.Combine(staging, Guid.NewGuid().ToString("N"))).FullName;
            var other = Directory.CreateDirectory(Path.Combine(staging, Guid.NewGuid().ToString("N"))).FullName;
            var rollback = Directory.CreateDirectory(Path.Combine(staging, "rollback-" + Guid.NewGuid().ToString("N"))).FullName;
            foreach (var dir in new[] { current, other, rollback }) File.WriteAllText(Path.Combine(dir, "payload.bin"), "keep unless explicitly discarded");
            Assert.IsTrue(UpdateApplyRequestHandler.DiscardUnlaunchedPayload(install, current));
            Assert.IsFalse(Directory.Exists(current));
            Assert.IsTrue(File.Exists(Path.Combine(other, "payload.bin")));
            Assert.IsTrue(File.Exists(Path.Combine(rollback, "payload.bin")));
            Assert.IsTrue(UpdateApplyRequestHandler.DiscardUnlaunchedPayload(install, current));
            Assert.IsFalse(UpdateApplyRequestHandler.DiscardUnlaunchedPayload(install, rollback));
            Assert.IsFalse(UpdateApplyRequestHandler.DiscardUnlaunchedPayload(install, staging));
            Assert.IsFalse(UpdateApplyRequestHandler.DiscardUnlaunchedPayload(install, install));
        }
        finally { Directory.Delete(install, true); }
    }

    [TestMethod]
    public void Discard_RejectsAValidLookingDirectoryOutsideThisInstallation()
    {
        var root = Directory.CreateTempSubdirectory("LertaroPayloadBoundary_").FullName;
        try
        {
            var install = Directory.CreateDirectory(Path.Combine(root, "install")).FullName;
            var outside = Directory.CreateDirectory(Path.Combine(root, "update-payload", Guid.NewGuid().ToString("N"))).FullName;
            File.WriteAllText(Path.Combine(outside, "user.txt"), "keep");
            Assert.IsFalse(UpdateApplyRequestHandler.DiscardUnlaunchedPayload(install, outside));
            Assert.IsTrue(File.Exists(Path.Combine(outside, "user.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}
