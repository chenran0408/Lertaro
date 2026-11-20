using Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

namespace Lertaro.Plugins.AudioDeviceSelector.Tests;

[TestClass]
public sealed class AudioDeviceDisplayFormatterTests
{
    [TestMethod]
    [DataRow("Speakers (Realtek(R) Audio)", "Speakers", "Realtek(R) Audio")]
    [DataRow("麦克风（USB (Pro)）", "麦克风", "USB (Pro)")]
    [DataRow("Output (2) (USB)", "Output (2)", "USB")]
    public void DescriptionModeKeepsTheEntireAdapterName(string friendly, string name, string adapter)
    {
        var display = AudioDeviceDisplayFormatter.Format(friendly, AudioDeviceDisplayMode.DeviceDescription);
        Assert.AreEqual(adapter, display.Title);
        Assert.AreEqual(name, display.Description);
    }

    [TestMethod]
    public void DescriptionModeWithoutAnAdapterKeepsAUsableTitle()
    {
        var display = AudioDeviceDisplayFormatter.Format("Studio Display", AudioDeviceDisplayMode.DeviceDescription);
        Assert.AreEqual("Studio Display", display.Title);
        Assert.AreEqual(string.Empty, display.Description);
    }

    [TestMethod]
    public void SplitFriendlyName_WithParenthesizedDescription_ReturnsBothParts()
    {
        var result = AudioDeviceDisplayFormatter.SplitFriendlyName("Speakers (USB Audio)");

        Assert.AreEqual("Speakers", result.Name);
        Assert.AreEqual("USB Audio", result.Description);
    }

    [TestMethod]
    public void SplitFriendlyName_WithoutDescription_KeepsFullName()
    {
        var result = AudioDeviceDisplayFormatter.SplitFriendlyName("Speakers");

        Assert.AreEqual("Speakers", result.Name);
        Assert.AreEqual(string.Empty, result.Description);
    }

    [TestMethod]
    public void Format_DeviceDescription_UsesDescriptionAsTitle()
    {
        var result = AudioDeviceDisplayFormatter.Format(
            "Speakers (USB Audio)", AudioDeviceDisplayMode.DeviceDescription);

        Assert.AreEqual("USB Audio", result.Title);
        Assert.AreEqual("Speakers", result.Description);
    }

    [TestMethod]
    public void TryParseQuery_OnlyAcceptsKeywordOrKeywordWithTerm()
    {
        Assert.IsTrue(AudioDeviceSelectorInstantProvider.TryParseQuery("ad", "ad", out var emptyTerm));
        Assert.AreEqual(string.Empty, emptyTerm);
        Assert.IsTrue(AudioDeviceSelectorInstantProvider.TryParseQuery("AD speakers", "ad", out var term));
        Assert.AreEqual("speakers", term);
        Assert.IsFalse(AudioDeviceSelectorInstantProvider.TryParseQuery("adapter", "ad", out _));
    }

    [TestMethod]
    public void DefaultDevice_UsesDistinctSuccessIcon()
    {
        Assert.AreNotEqual(
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Output, false),
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Output, true));
        Assert.AreEqual("SuccessBrush", AudioDeviceSelectorInstantProvider.GetIconColor(true));
        Assert.AreEqual("AccentBlue", AudioDeviceSelectorInstantProvider.GetIconColor(false));
    }

    [TestMethod]
    public void InputDevice_UsesMicrophoneIcon()
    {
        Assert.AreNotEqual(
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Output, false),
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Input, false));
        Assert.AreEqual(
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Output, true),
            AudioDeviceSelectorInstantProvider.GetIconData(AudioDeviceDirection.Input, true));
    }
}
