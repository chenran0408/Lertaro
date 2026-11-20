using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.Core.Services.LocalSend;
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

    [STATestMethod]
    public void ReceivingLinkOffersCopyAndOpenWhilePlainTextOffersCopyOnly()
    {
        using var theme = SettingsThemeFixture.Use();
        foreach (var preview in new[] { "https://example.test/share", "测试文本" })
        {
            var window = CreateReceiveWindow(preview);
            try
            {
                var copy = (Button)window.FindName("BtnCopyText");
                Assert.AreEqual(Lertaro.App.Services.TranslationManager.Instance["Settings_LocalSend_Copy"], copy.Content);
                var open = (Button)window.FindName("BtnOpenTextUrl");
                Assert.IsNotNull(open);
                Assert.AreEqual(preview.StartsWith("https://", StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed, open.Visibility);
            }
            finally { window.Close(); }
        }
    }

    [STATestMethod]
    public void ReceiveWindowIgnoresAnotherTransferAndLateCompletionAfterCancellation()
    {
        using var theme = SettingsThemeFixture.Use();
        var window = CreateReceiveWindow(null);
        try
        {
            var title = window.TxtWindowTitle.Text;
            window.HandleProgressChanged(Completion("other"));
            window.HandleSessionCanceled("other");
            DrainEvents();
            Assert.AreEqual(title, window.TxtWindowTitle.Text);
            Assert.IsTrue(window.BtnAcceptDefault.IsEnabled);
            window.HandleSessionCanceled("current");
            DrainEvents();
            var canceledTitle = window.TxtWindowTitle.Text;
            Assert.AreNotEqual(title, canceledTitle);
            window.HandleProgressChanged(Completion("current"));
            DrainEvents();
            Assert.AreEqual(canceledTitle, window.TxtWindowTitle.Text);
        }
        finally { window.Close(); }
    }

    private static LocalSendReceiveWindow CreateReceiveWindow(string? preview) => new(new LocalSendUploadRequestArgs("current",
        new PrepareUploadRequestDto
        {
            Files = new() { ["file"] = new() { FileName = "sample.txt", Size = 12, FileType = preview == null ? "application/octet-stream" : "text/plain", Preview = preview } }
        }, _ => { }));

    private static LocalSendProgressArgs Completion(string sessionId) =>
        new(sessionId, "sender", "file", "sample.txt", 12, 12, 1, 1, isFinished: true, isAllDone: true);

    private static void DrainEvents() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
