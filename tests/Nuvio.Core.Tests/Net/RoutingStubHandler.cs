using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace Nuvio.Core.Tests.Net;

internal sealed class RoutingStubHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _routes =
        new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public int RequestCount => Requests.Count;

    public void AddResponse(string host, string pathStartsWith, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[Key(host, pathStartsWith)] = (_, _) =>
            Task.FromResult(StubHttpMessageHandler.CreateResponse(status, body));
    }

    public void AddError(string host, HttpStatusCode status)
    {
        _routes[Key(host, "/")] = (_, _) => Task.FromResult(new HttpResponseMessage(status));
    }

    public void AddInfiniteDelay(string host)
    {
        _routes[Key(host, "/")] = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
    }

    public void AddHandler(string host, string pathStartsWith, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _routes[Key(host, pathStartsWith)] = (request, _) => Task.FromResult(handler(request));
    }

    public void AddHandlerAsync(
        string host,
        string pathStartsWith,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _routes[Key(host, pathStartsWith)] = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);

        var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI required.");
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? best = null;
        var bestLen = -1;
        foreach (var pair in _routes)
        {
            var (host, pathStartsWith) = ParseKey(pair.Key);
            if (!host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (uri.AbsolutePath.StartsWith(pathStartsWith, StringComparison.OrdinalIgnoreCase) &&
                pathStartsWith.Length > bestLen)
            {
                best = pair.Value;
                bestLen = pathStartsWith.Length;
            }
        }

        if (best is not null)
        {
            return best(request, cancellationToken);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static string Key(string host, string path) => $"{host.ToLowerInvariant()}|{path}";

    private static (string Host, string Path) ParseKey(string key)
    {
        var parts = key.Split('|', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : "/");
    }
}
