using System.Net;
using System.Net.Http;
using Lertaro.App.Services.Update;

namespace Lertaro.App.Tests.Services.Update;

[TestClass]
public sealed class UpdateCheckerTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(respond(request)); }
    }

    [TestMethod]
    public async Task BetaChecksForkAndChoosesHighestVersionRatherThanPublicationOrder()
    {
        using var handler = new Handler(request =>
        {
            Assert.AreEqual("https://api.github.com/repos/chenran0408/Lertaro/releases?per_page=50", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                [{"tag_name":"v5.10.0-beta9"},{"tag_name":"v5.9.9"},
                 {"tag_name":"v5.10.0-beta10"},{"tag_name":"v99.0.0","draft":true},
                 {"tag_name":"invalid"}]
                """) };
        });
        using var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, "5.10.0-beta3");
        Assert.AreEqual("v5.10.0-beta10", (await checker.CheckForUpdatesAsync())!.TagName);
        Assert.AreEqual("v5.10.0-beta10", (await checker.CheckForUpdatesAsync())!.TagName);
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task StableUsesLatestStableEndpointAndConcurrentRequestsShareOneCheck()
    {
        using var handler = new Handler(request =>
        {
            Assert.IsTrue(request.RequestUri!.AbsoluteUri.EndsWith("/chenran0408/Lertaro/releases/latest"));
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"tag_name":"v5.10.0"}""") };
        });
        using var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, "5.9.9");
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => checker.CheckForUpdatesAsync()));
        Assert.IsTrue(results.All(r => r!.TagName == "v5.10.0"));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow(403)]
    [DataRow(429)]
    public async Task RateLimitWaitsForServerResetInsteadOfRetryingEveryClick(int status)
    {
        var now = DateTimeOffset.UtcNow;
        var reset = now.AddMinutes(30);
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Headers.Add("X-RateLimit-Reset", reset.ToUnixTimeSeconds().ToString());
            return response;
        });
        using var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, "5.10.0-beta3", () => now);
        var error = await Assert.ThrowsAsync<UpdateCheckException>(() => checker.CheckForUpdatesAsync());
        Assert.IsTrue(error.RateLimited);
        Assert.AreEqual(reset.ToUnixTimeSeconds(), error.RetryAt.ToUnixTimeSeconds());
        await Assert.ThrowsAsync<UpdateCheckException>(() => checker.CheckForUpdatesAsync());
        Assert.AreEqual(1, handler.Calls);
        now = reset.AddSeconds(1);
        await Assert.ThrowsAsync<UpdateCheckException>(() => checker.CheckForUpdatesAsync());
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task RetryAfterTakesPriorityAndSuccessfulResultExpiresAfterFiveMinutes()
    {
        var now = DateTimeOffset.UtcNow;
        var busy = true;
        using var handler = new Handler(_ =>
        {
            if (!busy) return new(HttpStatusCode.OK) { Content = new StringContent("""{"tag_name":"v5.10.0"}""") };
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            response.Headers.Add("X-RateLimit-Reset", now.AddHours(1).ToUnixTimeSeconds().ToString());
            return response;
        });
        using var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, "5.9.9", () => now);
        var error = await Assert.ThrowsAsync<UpdateCheckException>(() => checker.CheckForUpdatesAsync());
        Assert.AreEqual(now.AddMinutes(2), error.RetryAt);
        now = now.AddMinutes(2); busy = false;
        Assert.AreEqual("v5.10.0", (await checker.CheckForUpdatesAsync())!.TagName);
        await checker.CheckForUpdatesAsync();
        Assert.AreEqual(2, handler.Calls);
        now = now.AddMinutes(5);
        await checker.CheckForUpdatesAsync();
        Assert.AreEqual(3, handler.Calls);
    }

    [TestMethod]
    public async Task InvalidJsonProducesFriendlyFailureAndShortCooldown()
    {
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("not json") });
        using var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, "5.9.9");
        var error = await Assert.ThrowsAsync<UpdateCheckException>(() => checker.CheckForUpdatesAsync());
        Assert.IsFalse(error.RateLimited);
        await Assert.ThrowsAsync<UpdateCheckException>(() => checker.CheckForUpdatesAsync());
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("v5.10.0-beta10", "5.10.0-beta9", true)]
    [DataRow("v5.10.0", "5.10.0-beta10", true)]
    [DataRow("v5.10.0-beta10", "5.10.0", false)]
    [DataRow("v5.9.9", "5.10.0-beta3", false)]
    [DataRow("v5.10.0-beta3", "5.10.0-beta3+hash", false)]
    [DataRow("v5.10.0-rc1", "5.10.0-beta10", true)]
    [DataRow("v5.10.0-beta.11", "5.10.0-beta10", true)]
    [DataRow("bad", "5.10.0-beta3", false)]
    public void VersionComparisonUnderstandsBetaAndStable(string candidate, string current, bool newer)
        => Assert.AreEqual(newer, UpdateVersion.IsNewer(candidate, current));
}
