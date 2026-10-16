using System.Text.Json;
using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class CompletedUpdatePayloadCleanupTests
{
    [TestMethod]
    public void SuccessfulVerdict_RemovesGuidStaging_ButKeepsRollbackAndOtherFolders()
    {
        var install = Directory.CreateTempSubdirectory("LertaroCompletedUpdate_").FullName;
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(install, "update-payload")).FullName;
            var old = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
            var current = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
            var rollback = Directory.CreateDirectory(Path.Combine(root, "rollback-" + Guid.NewGuid().ToString("N"))).FullName;
            var unrelated = Directory.CreateDirectory(Path.Combine(root, "my-files")).FullName;
            foreach (var directory in new[] { old, current, rollback, unrelated })
                File.WriteAllText(Path.Combine(directory, "content.txt"), "keep unless staging");

            WriteVerdict(install, "success");
            using (UpdateApplyRequestHandler.AcquireUpdateLock(install))
                Assert.Throws<IOException>(() => CompletedUpdatePayloadCleanup.Clean(install));
            Assert.AreEqual(2, CompletedUpdatePayloadCleanup.Clean(install));
            Assert.IsFalse(Directory.Exists(old));
            Assert.IsFalse(Directory.Exists(current));
            foreach (var directory in new[] { rollback, unrelated })
                Assert.IsTrue(File.Exists(Path.Combine(directory, "content.txt")), directory);
            Assert.AreEqual(0, CompletedUpdatePayloadCleanup.Clean(install));
        }
        finally { Directory.Delete(install, true); }
    }

    [TestMethod]
    public void FailedOrMissingVerdict_KeepsStagingForRecovery()
    {
        var install = Directory.CreateTempSubdirectory("LertaroFailedUpdate_").FullName;
        try
        {
            var stage = Directory.CreateDirectory(Path.Combine(install, "update-payload", Guid.NewGuid().ToString("N"))).FullName;
            Assert.AreEqual(0, CompletedUpdatePayloadCleanup.Clean(install));
            WriteVerdict(install, "failed");
            Assert.AreEqual(0, CompletedUpdatePayloadCleanup.Clean(install));
            Assert.IsTrue(Directory.Exists(stage));
        }
        finally { Directory.Delete(install, true); }
    }

    [TestMethod]
    public async Task ServiceStartupCleanup_WaitsUntilUpdaterWritesSuccessAndReleasesLock()
    {
        var install = Directory.CreateTempSubdirectory("LertaroUpdatingAtStartup_").FullName;
        try
        {
            var stage = Directory.CreateDirectory(Path.Combine(install, "update-payload", Guid.NewGuid().ToString("N"))).FullName;
            using (UpdateApplyRequestHandler.AcquireUpdateLock(install))
            {
                CompletedUpdatePayloadCleanup.Schedule(install);
                WriteVerdict(install, "success");
                await Task.Delay(650);
                Assert.IsTrue(Directory.Exists(stage), "Cleanup must wait for the update lease.");
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (Directory.Exists(stage))
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(50, timeout.Token);
            }
        }
        finally { Directory.Delete(install, true); }
    }

    private static void WriteVerdict(string install, string status) => File.WriteAllText(
        Path.Combine(install, UpdateResult.FileName),
        JsonSerializer.Serialize(new UpdateResult { Status = status, Time = DateTimeOffset.UtcNow }));
}
