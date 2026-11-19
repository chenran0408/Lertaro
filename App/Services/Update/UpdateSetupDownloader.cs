using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Lertaro.Core.Services.Update;

namespace Lertaro.App.Services.Update;

/// <summary>Downloads a release asset to the user's Downloads folder.</summary>
public class UpdateSetupDownloader
{
    private static readonly Lazy<UpdateSetupDownloader> _instance =
        new(() => new UpdateSetupDownloader(CreateClient(), TimeSpan.FromSeconds(30)));
    public static UpdateSetupDownloader Instance => _instance.Value;

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _idleTimeout;

    internal UpdateSetupDownloader(HttpClient httpClient, TimeSpan idleTimeout)
    {
        _httpClient = httpClient;
        _idleTimeout = idleTimeout;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lertaro", "1.0.0"));
        return client;
    }

    public Task<string> DownloadAsync(string url, string fileName, string? signatureUrl, Action<int> reportProgress, CancellationToken cancellationToken) =>
        DownloadToAsync(url, ResolveTargetPath(fileName), reportProgress, cancellationToken, signatureUrl);

    internal async Task<string> DownloadToAsync(
        string url, string target, Action<int> reportProgress, CancellationToken cancellationToken, string? signatureUrl = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_idleTimeout);
        var temporary = $"{target}.{Guid.NewGuid():N}.partial";
        try
        {
            var signature = Path.GetExtension(target).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                ? await DownloadSignatureAsync(signatureUrl, timeout.Token).ConfigureAwait(false)
                : null;
            timeout.CancelAfter(_idleTimeout);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            await using var content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 8192, useAsync: true))
            {
                var buffer = new byte[8192];
                long totalRead = 0;
                var reportedPercent = -1;
                int read;
                while ((read = await content.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    timeout.CancelAfter(_idleTimeout);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    totalRead += read;
                    if (contentLength is > 0)
                    {
                        var percent = Math.Min(100, (int)(100.0 * totalRead / contentLength.Value));
                        if (percent != reportedPercent)
                        {
                            reportedPercent = percent;
                            reportProgress(percent);
                        }
                    }
                }

                if (totalRead == 0 || (contentLength.HasValue && totalRead != contentLength.Value))
                    throw new IOException("The update download ended before the complete file was received.");

                timeout.Token.ThrowIfCancellationRequested();
                timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if (signature != null)
                {
                    output.Position = 0;
                    if (!UpdatePackage.Verify(output, signature))
                        throw new CryptographicException("The portable update failed signature verification.");
                }
            }

            timeout.Token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
            return target;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The update download stopped making progress.", ex);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private async Task<byte[]> DownloadSignatureAsync(string? url, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new CryptographicException("The portable update has no matching signature.");

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new CryptographicException("The portable update signature could not be found.");
        response.EnsureSuccessStatusCode();

        await using var content = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[UpdatePackage.MaxSignatureBytes + 1];
        var length = await content.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken: token).ConfigureAwait(false);
        if (length == 0 || length > UpdatePackage.MaxSignatureBytes)
            throw new CryptographicException("The portable update signature has an invalid size.");
        return bytes[..length];
    }

    /// <summary>Uses the profile's Downloads folder when it exists, otherwise the temporary directory.</summary>
    private static string ResolveTargetPath(string fileName)
    {
        string? directory = null;
        try
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } profile
                && Path.Combine(profile, "Downloads") is var candidate
                && Directory.Exists(candidate)
                ? candidate
                : null;
        }
        catch
        {
            // Fall through to temp.
        }
        directory ??= Path.GetTempPath();
        return Path.Combine(directory, fileName);
    }
}
