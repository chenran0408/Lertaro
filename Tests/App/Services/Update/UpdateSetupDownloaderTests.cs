using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Lertaro.App.Services.Update;

namespace Lertaro.App.Tests.Services.Update;

[TestClass]
public sealed class UpdateSetupDownloaderTests
{
    [TestMethod]
    public async Task DownloadToAsync_ReplacesExistingFileAfterReceivingCompleteResponse()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var target = Path.Combine(folder.FullName, "Lertaro-Setup.exe");
            await File.WriteAllTextAsync(target, "old");
            using var client = new HttpClient(new ContentHandler(new ByteArrayContent([1, 2, 3])));
            var downloader = new UpdateSetupDownloader(client, TimeSpan.FromSeconds(5));
            var progress = new List<int>();

            var downloaded = await downloader.DownloadToAsync("https://example.test/update", target, progress.Add, CancellationToken.None);

            Assert.AreEqual(target, downloaded);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(target));
            CollectionAssert.AreEqual(new[] { 100 }, progress);
            Assert.HasCount(1, folder.GetFiles());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(10)]
    public async Task DownloadToAsync_WhenResponseIsEmpty_KeepsPreviousFile(int advertisedLength)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var target = Path.Combine(folder.FullName, "Lertaro-Setup.exe");
            await File.WriteAllTextAsync(target, "old");
            using var client = new HttpClient(new ContentHandler(new ByteArrayContent([])
            {
                Headers = { ContentLength = advertisedLength }
            }));
            var downloader = new UpdateSetupDownloader(client, TimeSpan.FromSeconds(5));

            await Assert.ThrowsExactlyAsync<IOException>(() =>
                downloader.DownloadToAsync("https://example.test/update", target, _ => { }, CancellationToken.None));

            Assert.AreEqual("old", await File.ReadAllTextAsync(target));
            Assert.HasCount(1, folder.GetFiles());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DownloadToAsync_WhenBodyStalls_PreservesPreviousFileAndRemovesPartialFile(bool userCancels)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var target = Path.Combine(folder.FullName, "Lertaro-Setup.exe");
            await File.WriteAllTextAsync(target, "old");
            using var client = new HttpClient(new ContentHandler(new StreamContent(new StallingStream())
            {
                Headers = { ContentLength = 12 }
            }));
            var downloader = new UpdateSetupDownloader(client, userCancels ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(500));
            using var cancellation = new CancellationTokenSource();
            var progress = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var download = downloader.DownloadToAsync("https://example.test/update", target,
                percent => progress.TrySetResult(percent), cancellation.Token);

            Assert.AreEqual(25, await progress.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            if (userCancels)
            {
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => download);
            }
            else
            {
                await Assert.ThrowsExactlyAsync<TimeoutException>(() => download);
            }

            Assert.AreEqual("old", await File.ReadAllTextAsync(target));
            Assert.HasCount(1, folder.GetFiles());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("tampered")]
    [DataRow("malformed")]
    [DataRow("missing")]
    public async Task DownloadToAsync_PublishesPortablePackageOnlyWhenSigned(string scenario)
    {
        // Public fixture from ForkSigningKeyTests, signed by the production release key.
        var payload = Convert.FromBase64String("TGVydGFybyBmb3JrIHJlbGVhc2Ugc2lnbmluZyByZWdyZXNzaW9uIGZpeHR1cmUgMjAyNi0xMC0xNg==");
        var signature = Convert.FromBase64String("MEQCIBuKXyUmmd4srOV92FRnZDwv7H+N6uYdVU0uMr8pgYRhAiA2pao8SKb3bLZ1kUTumzUP0yUm1tOQ3S3if/O/IODeiw==");
        if (scenario == "tampered")
            payload[0] ^= 1;
        if (scenario == "malformed")
            signature = [1, 2, 3];

        var folder = Directory.CreateTempSubdirectory();
        try
        {
            const string zipUrl = "https://example.test/Lertaro-Portable.zip";
            var target = Path.Combine(folder.FullName, "Lertaro-Portable.zip");
            await File.WriteAllTextAsync(target, "old");
            using var client = new HttpClient(new ContentHandler(new ByteArrayContent(payload), new ByteArrayContent(signature)));
            var downloader = new UpdateSetupDownloader(client, TimeSpan.FromSeconds(5));
            var download = downloader.DownloadToAsync(zipUrl, target, _ => { }, CancellationToken.None,
                scenario == "missing" ? null : zipUrl + ".sig");

            if (scenario == "valid")
            {
                Assert.AreEqual(target, await download);
                CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(target));
            }
            else
            {
                await Assert.ThrowsExactlyAsync<CryptographicException>(() => download);
                Assert.AreEqual("old", await File.ReadAllTextAsync(target));
            }

            Assert.HasCount(1, folder.GetFiles());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private sealed class ContentHandler(HttpContent content, HttpContent? signature = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal) ? signature : content
            });
    }

    private sealed class StallingStream() : MemoryStream([1, 2, 3])
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length)
                return await base.ReadAsync(buffer, cancellationToken);

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
