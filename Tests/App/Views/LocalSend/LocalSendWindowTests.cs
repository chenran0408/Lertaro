using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Tests.Views.Settings;
using Lertaro.App.ViewModels.LocalSend;
using Lertaro.App.Views.LocalSend;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.Tests.Views.LocalSend;

[TestClass, DoNotParallelize]
public sealed class LocalSendWindowTests
{
    [STATestMethod]
    public void SendWindow_EditContentReturnsToTheDraft()
    {
        using var theme = SettingsThemeFixture.Use();
        var window = new LocalSendSendWindow(initialText: "Transfer draft");
        try
        {
            var viewModel = (LocalSendSendViewModel)window.DataContext;
            Assert.AreEqual(Visibility.Visible, window.DevicePanel.Visibility);
            window.DevicePanel.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.AreEqual(Visibility.Visible, window.CollectPanel.Visibility);
            Assert.AreEqual(Visibility.Collapsed, window.DevicePanel.Visibility);
            Assert.IsTrue(viewModel.IsTextMode);
            Assert.AreEqual("Transfer draft", viewModel.TextToSend);
            Assert.IsTrue(viewModel.CanGoNextStep);
        }
        finally
        {
            window.Close();
        }
    }

    [STATestMethod]
    public void ReceiveWindow_CanClearAndRestoreAllFileSelections()
    {
        using var theme = SettingsThemeFixture.Use();
        var request = new LocalSendUploadRequestArgs(string.Empty, new PrepareUploadRequestDto
        {
            Files = new Dictionary<string, LocalSendFileDto>
            {
                ["first"] = new() { FileName = "First.txt", Size = 1024 },
                ["second"] = new() { FileName = "Second.txt", Size = 2048 }
            }
        }, _ => { });
        var window = new LocalSendReceiveWindow(request);
        try
        {
            Assert.HasCount(2, window.LstFiles.SelectedItems);
            Assert.IsTrue(window.BtnAcceptDefault.IsEnabled);
            window.BtnToggleSelectAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.HasCount(0, window.LstFiles.SelectedItems);
            Assert.IsFalse(window.BtnAcceptDefault.IsEnabled);
            Assert.IsFalse(window.BtnSaveTo.IsEnabled);

            window.BtnToggleSelectAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.HasCount(2, window.LstFiles.SelectedItems);
            Assert.IsTrue(window.BtnAcceptDefault.IsEnabled);
            Assert.IsTrue(window.BtnSaveTo.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }
}
