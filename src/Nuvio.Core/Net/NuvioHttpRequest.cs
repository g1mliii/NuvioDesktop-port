namespace Nuvio.Core.Net;

public sealed record NuvioHttpRequest(
    Uri Uri,
    long MaxBytes,
    TimeSpan Timeout,
    string ResourceKind,
    string? AddonId = null);

public sealed record NuvioHttpResponse(
    string Body,
    int StatusCode,
    Uri FinalUri);
