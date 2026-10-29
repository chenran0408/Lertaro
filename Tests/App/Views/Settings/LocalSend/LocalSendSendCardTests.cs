using System.Windows;
using System.Windows.Controls;
using Lertaro.App.ViewModels.Settings.LocalSend;
using Lertaro.App.Views.Settings.LocalSend;
using Lertaro.App.Tests.Views.Settings;
using Lertaro.Core;

namespace Lertaro.App.Tests.Views.Settings.LocalSend;

[TestClass]
public sealed class LocalSendSendCardTests
{
    [StaTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Card_EnablesSendingOnlyForAnEnabledService(bool enabled)
    {
        using var theme = SettingsThemeFixture.Use();
        var card = Create(enabled);
        card.Measure(new Size(700, 400));
        card.Arrange(new Rect(0, 0, 700, 400));
        card.UpdateLayout();
        var sendButton = (Button)card.FindName("SendFilesButton");
        Assert.AreEqual(enabled, sendButton.IsEnabled);
    }

    [TestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    public void GetDropEffect_OnlyAcceptsFilesWhenTheServiceIsEnabled(bool enabled, bool files, bool accepted) => Assert.AreEqual(accepted ? DragDropEffects.Copy : DragDropEffects.None, LocalSendSendCard.GetDropEffect(enabled, files));

    [StaTestMethod]
    public void SettingsPage_BuildsAndContainsTheSendingEntry()
    {
        using var theme = SettingsThemeFixture.Use();
        var page = new LocalSendSettingsPage { DataContext = new LocalSendSettingsViewModel(new UserSettings()) };
        page.Measure(new Size(700, 900));
        page.Arrange(new Rect(0, 0, 700, 900));
        page.UpdateLayout();
        Assert.IsInstanceOfType<LocalSendSendCard>(page.FindName("SendEntry"));
    }

    private static LocalSendSendCard Create(bool enabled)
    {
        var settings = new UserSettings();
        settings.LocalSend.Enabled = enabled;
        return new LocalSendSendCard { DataContext = new LocalSendSettingsViewModel(settings) };
    }
}
