namespace Nuvio.Core.Net;

public interface INuvioHttpClient
{
    Task<NuvioHttpResponse> GetStringAsync(NuvioHttpRequest request, CancellationToken cancellationToken);
}
