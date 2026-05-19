using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace Nuvio.Core.Tests.Net;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public int RequestCount => Requests.Count;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : this((request, _) => Task.FromResult(handler(request)))
    {
    }

    public StubHttpMessageHandler(HttpStatusCode status, string body)
        : this(_ => CreateResponse(status, body))
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        return await _handler(request, cancellationToken).ConfigureAwait(false);
    }

    public static HttpResponseMessage CreateResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    public static HttpResponseMessage CreateRedirect(HttpStatusCode status, Uri target)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = target;
        return response;
    }
}
