using System.Security.Cryptography;
using Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests.Engine.JsonRpc;

[TestClass]
public sealed class FlowRuntimeIntegrityTests
{
    [TestMethod]
    public void ArchiveIntegrity_AcceptsExpectedBytes_RejectsTampering()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "fixture archive");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            FlowRuntimeIntegrity.VerifyArchive(file, hash);
            File.AppendAllText(file, "tampered");
            Assert.Throws<InvalidDataException>(() => FlowRuntimeIntegrity.VerifyArchive(file, hash));
        }
        finally { File.Delete(file); }
    }
}
