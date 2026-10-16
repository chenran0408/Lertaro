using Lertaro.Core.Services.Update;

namespace Lertaro.Core.Tests.Services.Update;

[TestClass]
public sealed class ForkSigningKeyTests
{
    // A public fixture signed once by this fork's release key; no private key belongs in the tests.
    private const string Payload = "TGVydGFybyBmb3JrIHJlbGVhc2Ugc2lnbmluZyByZWdyZXNzaW9uIGZpeHR1cmUgMjAyNi0xMC0xNg==";
    private const string Signature = "MEQCIBuKXyUmmd4srOV92FRnZDwv7H+N6uYdVU0uMr8pgYRhAiA2pao8SKb3bLZ1kUTumzUP0yUm1tOQ3S3if/O/IODeiw==";

    [TestMethod]
    public void ProductionKeyAcceptsForkSignatureAndRejectsTamperingAndUnsignedPlaceholder()
    {
        var root = Directory.CreateTempSubdirectory("LertaroForkSignature-").FullName;
        var payloadPath = Path.Combine(root, "payload.bin");
        var signaturePath = payloadPath + ".sig";
        try
        {
            var bytes = Convert.FromBase64String(Payload);
            File.WriteAllBytes(payloadPath, bytes);
            File.WriteAllBytes(signaturePath, Convert.FromBase64String(Signature));
            Assert.IsTrue(UpdatePackage.Verify(payloadPath, signaturePath));
            bytes[0] ^= 1;
            File.WriteAllBytes(payloadPath, bytes);
            Assert.IsFalse(UpdatePackage.Verify(payloadPath, signaturePath));
            File.WriteAllBytes(payloadPath, Convert.FromBase64String(Payload));
            File.WriteAllText(signaturePath, "unsigned");
            Assert.IsFalse(UpdatePackage.Verify(payloadPath, signaturePath));
        }
        finally { Directory.Delete(root, true); }
    }
}
