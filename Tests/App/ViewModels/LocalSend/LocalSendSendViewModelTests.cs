using System.Net;
using System.Net.Sockets;
using System.Text;
using Lertaro.Core.Services.LocalSend.Models;
using System.IO;
using Lertaro.App.ViewModels.LocalSend;

namespace Lertaro.App.Tests.ViewModels.LocalSend;

[TestClass]
public sealed class LocalSendSendViewModelTests
{
    [TestMethod]
    public void SetMode_WithText_SwitchesExistingItemRequestToTextDeviceSelection()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var viewModel = new LocalSendSendViewModel(new[] { path });

            viewModel.SetMode(LocalSendSendMode.Text, "hello", proceed: true);

            Assert.IsTrue(viewModel.IsTextMode);
            Assert.AreEqual("hello", viewModel.TextToSend);
            Assert.IsEmpty(viewModel.TargetFiles);
            Assert.AreEqual(1, viewModel.CurrentStep);
            Assert.IsTrue(viewModel.IsFromAction);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void EditingFilesAndReturningToTextKeepsTheMessageDraft()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var model = new LocalSendSendViewModel(initialText: "未发送的草稿");
            model.ReturnToStep0();
            model.AddPaths([path]);
            model.ProceedToStep1();
            CollectionAssert.AreEqual(new[] { path }, model.TargetFiles.ToArray());
            model.ReturnToStep0();
            model.SelectedMode = 1;
            Assert.AreEqual("未发送的草稿", model.TextToSend);
            Assert.IsTrue(model.CanGoNextStep);
            Assert.IsEmpty(model.TargetFiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task SendingToTwoRecipientsKeepsTheFirstDeclineAfterTheLastAccepts()
    {
        using var first = new TcpListener(IPAddress.Loopback, 0);
        using var second = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        second.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var model = new LocalSendSendViewModel(initialText: "local test message");
        var completed = false;
        model.SendSuccessCompleted += (_, _) => completed = true;
        var firstReplies = ReplyAsync(first, "403 Forbidden", "204 No Content");
        var secondReply = ReplyAsync(second, "204 No Content");
        await model.StartSendBatchAsync([Device(first, "first"), Device(second, "second")]).WaitAsync(deadline.Token);
        Assert.AreEqual(LocalSendSendResult.Declined, model.LastSendResult);
        Assert.IsFalse(completed);
        Assert.IsTrue(model.CanRetryFailed);
        Assert.AreEqual(1, model.SentDeviceCount);
        Assert.AreEqual(2, model.TargetDeviceCount);

        await Task.WhenAll(model.RetryFailedAsync(), model.RetryFailedAsync()).WaitAsync(deadline.Token);
        await Task.WhenAll(firstReplies, secondReply);
        Assert.IsFalse(model.CanRetryFailed);
        Assert.IsTrue(completed);
        Assert.AreEqual(2, model.SentDeviceCount);
        Assert.AreEqual(2, model.TargetDeviceCount);
        Assert.IsFalse(second.Pending());

        static LocalSendSendDeviceItem Device(TcpListener receiver, string alias) =>
            new(new LocalSendDeviceInfo { Alias = alias, IpAddress = "127.0.0.1",
                Port = ((IPEndPoint)receiver.LocalEndpoint).Port, Protocol = "http" });

        async Task ReplyAsync(TcpListener receiver, params string[] statuses)
        {
            foreach (var status in statuses)
            {
                using var peer = await receiver.AcceptTcpClientAsync(deadline.Token);
                using var stream = peer.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var length = 0;
                while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } line)
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        length = int.Parse(line["Content-Length:".Length..]);
                }
                Assert.AreEqual(length, await reader.ReadBlockAsync(new char[length], deadline.Token));
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"), deadline.Token);
            }
        }
    }

    [TestMethod]
    public void Constructor_WithEmptyTextMode_OpensTextCollectionStep()
    {
        using var viewModel = new LocalSendSendViewModel(initialMode: LocalSendSendMode.Text);

        Assert.IsTrue(viewModel.IsTextMode);
        Assert.AreEqual(0, viewModel.CurrentStep);
        Assert.IsFalse(viewModel.CanGoNextStep);
    }

    [TestMethod]
    public void SetMode_WithEmptyText_ClearsPreviousText()
    {
        using var viewModel = new LocalSendSendViewModel(initialText: "previous");

        viewModel.SetMode(LocalSendSendMode.Text);

        Assert.AreEqual(string.Empty, viewModel.TextToSend);
        Assert.AreEqual(0, viewModel.CurrentStep);
        Assert.IsFalse(viewModel.CanGoNextStep);
    }
}
