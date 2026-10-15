using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lertaro.App.Services.Update;

public class UpdateChecker
{
    public const string ReleasesPageUrl = "https://github.com/chenran0408/Lertaro/releases";
    private const string ApiRoot = "https://api.github.com/repos/chenran0408/Lertaro/releases";
    private static readonly Lazy<UpdateChecker> _instance = new(() => new(CreateClient(), UpdateVersion.Current));
    public static UpdateChecker Instance => _instance.Value;
    private readonly HttpClient _httpClient;
    private readonly string _currentVersion;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextCheck;
    private GitHubReleaseInfo? _cached;
    private UpdateCheckException? _failure;

    internal UpdateChecker(HttpClient client, string currentVersion, Func<DateTimeOffset>? clock = null)
    {
        _httpClient = client; _currentVersion = currentVersion; _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lertaro", "1.0"));
        return client;
    }

    public async Task<GitHubReleaseInfo?> CheckForUpdatesAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_clock() < _nextCheck)
            {
                if (_failure != null) throw _failure;
                return _cached;
            }
            using var response = await _httpClient.GetAsync(ApiRoot +
                (UpdateVersion.IsPreview(_currentVersion) ? "?per_page=50" : "/latest")).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var retry = _clock().AddMinutes(1);
                if (response.Headers.RetryAfter?.Delta is { } delta) retry = _clock() + delta;
                else if (response.Headers.RetryAfter?.Date is { } date) retry = date;
                else if (response.Headers.TryGetValues("X-RateLimit-Reset", out var values)
                    && long.TryParse(values.FirstOrDefault(), out var seconds)
                    && seconds >= 0 && seconds <= 253402300799) retry = DateTimeOffset.FromUnixTimeSeconds(seconds);
                _nextCheck = retry > _clock() ? retry : _clock().AddMinutes(1);
                _failure = new UpdateCheckException(true, _nextCheck);
                throw _failure;
            }
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var releases = UpdateVersion.IsPreview(_currentVersion)
                ? JsonSerializer.Deserialize<GitHubReleaseInfo[]>(json, options) ?? []
                : JsonSerializer.Deserialize<GitHubReleaseInfo>(json, options) is { } release ? [release] : [];
            _cached = releases.Where(r => r != null && !r.Draft && UpdateVersion.Compare(r.TagName, r.TagName) == 0)
                .OrderByDescending(r => r.TagName, Comparer<string>.Create((a, b) => UpdateVersion.Compare(a, b) ?? 0)).FirstOrDefault();
            _failure = null;
            _nextCheck = _clock().AddMinutes(5);
            return _cached;
        }
        catch (UpdateCheckException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Core.Logger.Log($"[UpdateChecker] Check failed: {ex}", Core.LogLevel.Warn);
            _nextCheck = _clock().AddMinutes(1);
            _failure = new UpdateCheckException(false, _nextCheck);
            throw _failure;
        }
        finally { _gate.Release(); }
    }
}

internal sealed class UpdateCheckException(bool rateLimited, DateTimeOffset retryAt) : Exception("Update check temporarily unavailable.")
{
    internal bool RateLimited { get; } = rateLimited;
    internal DateTimeOffset RetryAt { get; } = retryAt;
}
public class GitHubReleaseInfo
{
    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}
