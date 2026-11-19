using System.Runtime.InteropServices;

namespace Lertaro.Plugins.ClipboardHistory.Tests;

[TestClass]
public sealed class PasteBackHelperTests
{
    [TestMethod]
    [DataRow(0u, 0)]
    [DataRow(1u, 1)]
    [DataRow(2u, 2)]
    [DataRow(3u, 1)]
    [DataRow(4u, 0)]
    public void PartialSendReleasesOnlyKeysAlreadyPressed(uint sent, int releases)
    {
        var cleanup = PasteBackHelper.PendingKeyReleases(PasteBackHelper.BuildInputs(), sent);
        Assert.HasCount(releases, cleanup);
        Assert.IsTrue(cleanup.All(input => input.Data.Keyboard.Flags == 2));
        if (releases == 1) Assert.AreEqual((ushort)0x11, cleanup[0].Data.Keyboard.VirtualKey);
    }

    [TestMethod]
    public void BuildsFourKeyboardInputsWithMatchingKeyReleases()
    {
        var inputs = PasteBackHelper.BuildInputs();
        Assert.HasCount(4, inputs);
        CollectionAssert.AreEqual(new ushort[] { 0x11, 0x56, 0x56, 0x11 },
            inputs.Select(input => input.Data.Keyboard.VirtualKey).ToArray());
        CollectionAssert.AreEqual(new uint[] { 0, 0, 2, 2 },
            inputs.Select(input => input.Data.Keyboard.Flags).ToArray());
        Assert.IsTrue(inputs.All(input => input.Type == 1));
        Assert.AreEqual(nint.Size == 8 ? 40 : 28, Marshal.SizeOf<PasteBackHelper.Input>());
    }
}
