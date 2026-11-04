using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Lertaro.App.Services.Update;

/// <summary>
/// Downloads the release's setup executable to the user's Downloads folder. This is the whole update
/// path now: no service handoff, no updater script, no file replacement while the app runs. The user
/// runs the setup themselves when the app tells them it is ready, which sidesteps every failure class
/// the silent path collected (antivirus locking freshly written binaries, service stops, rollback
/// debris) at the cost of one click the user owns end to end.
/// </summary>
public class UpdateSetupDownloader
{
    private static readonly Lazy<UpdateSetupDownloader> _instance = new(() => new UpdateSetupDownloader());
    public static UpdateSetupDownloader Instance => _instance.Value;

    private readonly HttpClient _httpClient;

    private UpdateSetupDownloader()
    {
        // No timeout: a 100 MB installer on a slow link legitimately takes minutes. The caller owns
        // cancellation, and progress reporting keeps a long download from looking stuck.
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lertaro", "1.0.0"));
    }

    /// <summary>Downloads the setup to the user's Downloads folder (fallback: temp) and returns its
    /// full path. Throws on network/IO failure; the caller turns that into the failure message.</summary>
    public async Task<string> DownloadAsync(string setupUrl, string fileName, Action<double>? progressCallback = null)
    {
        var target = ResolveTargetPath(fileName);
        using var response = await _httpClient.GetAsync(setupUrl, HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var fileStream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

        var buffer = new byte[8192];
        long totalRead = 0;
        int read;
        while ((read = await contentStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            totalRead += read;
            if (totalBytes > 0)
                progressCallback?.Invoke((double)totalRead / totalBytes);
        }

        return target;
    }

    /// <summary>Downloads folder, or temp when the known-folders API has nothing (some stripped-down
    /// or redirected profiles). A fixed name per version means a re-download of the same release
    /// overwrites its own leftover instead of accumulating copies.</summary>
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
