using System.Net;
using System.Net.Http;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Net;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Tests.Net;

public sealed class NuvioHttpClientTests
{
    [Fact]
    public async Task GetStringAsync_ReturnsBody()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{\"id\":\"ok\"}");
        var client = CreateClient(handler);

        var response = await client.GetStringAsync(BuildRequest("https://addons.example.test/manifest.json"), CancellationToken.None);

        Assert.Equal("{\"id\":\"ok\"}", response.Body);
        Assert.Equal(200, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetStringAsync_RejectsHttpUrl()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<NuvioValidationException>(() =>
            client.GetStringAsync(BuildRequest("http://addons.example.test/manifest.json"), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetStringAsync_FailsWhenResponseExceedsMaxBytes()
    {
        var oversized = new string('x', 1024);
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, oversized);
        var client = CreateClient(handler);

        var request = BuildRequest("https://addons.example.test/manifest.json") with { MaxBytes = 128 };
        await Assert.ThrowsAsync<NuvioValidationException>(() =>
            client.GetStringAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetStringAsync_FollowsSameHostRedirect()
    {
        var attempts = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                return Task.FromResult(StubHttpMessageHandler.CreateRedirect(
                    HttpStatusCode.Redirect,
                    new Uri("https://addons.example.test/v2/manifest.json")));
            }

            return Task.FromResult(StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "{\"id\":\"redirected\"}"));
        });

        var client = CreateClient(handler);
        var response = await client.GetStringAsync(BuildRequest("https://addons.example.test/manifest.json"), CancellationToken.None);

        Assert.Equal("{\"id\":\"redirected\"}", response.Body);
        Assert.Equal("https://addons.example.test/v2/manifest.json", response.FinalUri.ToString());
    }

    [Fact]
    public async Task GetStringAsync_RejectsCrossHostRedirect()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(StubHttpMessageHandler.CreateRedirect(
                HttpStatusCode.Redirect,
                new Uri("https://evil.example.test/manifest.json"))));

        var client = CreateClient(handler);
        await Assert.ThrowsAsync<NuvioValidationException>(() =>
            client.GetStringAsync(BuildRequest("https://addons.example.test/manifest.json"), CancellationToken.None));
    }

    [Fact]
    public async Task GetStringAsync_RejectsHttpsToHttpRedirect()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            Task.FromResult(StubHttpMessageHandler.CreateRedirect(
                HttpStatusCode.Redirect,
                new Uri("http://addons.example.test/manifest.json"))));

        var client = CreateClient(handler);
        await Assert.ThrowsAsync<NuvioValidationException>(() =>
            client.GetStringAsync(BuildRequest("https://addons.example.test/manifest.json"), CancellationToken.None));
    }

    [Fact]
    public async Task GetStringAsync_RetriesOnceOn5xx()
    {
        var attempts = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? StubHttpMessageHandler.CreateResponse(HttpStatusCode.ServiceUnavailable, "fail")
                : StubHttpMessageHandler.CreateResponse(HttpStatusCode.OK, "{\"id\":\"retried\"}"));
        });

        using var httpClient = new HttpClient(handler);
        var nuvio = new NuvioHttpClient(
            httpClient,
            throttler: null,
            diagnostics: null,
            timeProvider: null,
            backoffDelays: [TimeSpan.Zero, TimeSpan.Zero]);

        var response = await nuvio.GetStringAsync(
            BuildRequest("https://addons.example.test/manifest.json"),
            CancellationToken.None);

        Assert.Equal("{\"id\":\"retried\"}", response.Body);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task GetStringAsync_StopsAfterRetryBudgetOn5xx()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "fail");
        using var httpClient = new HttpClient(handler);
        var nuvio = new NuvioHttpClient(
            httpClient,
            throttler: null,
            diagnostics: null,
            timeProvider: null,
            backoffDelays: [TimeSpan.Zero, TimeSpan.Zero]);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            nuvio.GetStringAsync(BuildRequest("https://addons.example.test/manifest.json"), CancellationToken.None));
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task GetStringAsync_RecordsDiagnosticsOnSuccess()
    {
        var diagnostics = new NetworkDiagnostics();
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler, diagnostics: diagnostics);

        await client.GetStringAsync(BuildRequest("https://addons.example.test/manifest.json"), CancellationToken.None);

        var snapshot = diagnostics.Snapshot();
        Assert.Single(snapshot);
        Assert.Equal(NetworkEventKind.Success, snapshot[0].Kind);
        Assert.Equal("addons.example.test", snapshot[0].Host);
        Assert.Equal(200, snapshot[0].StatusCode);
    }

    private static NuvioHttpClient CreateClient(HttpMessageHandler handler, INetworkDiagnostics? diagnostics = null)
    {
        var httpClient = new HttpClient(handler);
        return new NuvioHttpClient(httpClient, throttler: null, diagnostics: diagnostics);
    }

    private static NuvioHttpRequest BuildRequest(string url) =>
        new(new Uri(url), MaxBytes: 64 * 1024, Timeout: TimeSpan.FromSeconds(5), ResourceKind: "manifest");
}
