using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using Nuvio.Core.Addons;
using Nuvio.Core.Diagnostics;
using Nuvio.Core.Security;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Net;

public sealed class NuvioHttpClient : INuvioHttpClient
{
    public static IReadOnlyList<TimeSpan> DefaultBackoffDelays { get; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private static readonly string DefaultUserAgent = BuildUserAgent();

    private readonly HttpClient _httpClient;
    private readonly PerHostThrottler _throttler;
    private readonly INetworkDiagnostics? _diagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<TimeSpan> _backoffDelays;
    private readonly int _maxAttempts;

    public NuvioHttpClient(
        HttpClient httpClient,
        PerHostThrottler? throttler = null,
        INetworkDiagnostics? diagnostics = null,
        TimeProvider? timeProvider = null,
        IReadOnlyList<TimeSpan>? backoffDelays = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _throttler = throttler ?? new PerHostThrottler();
        _diagnostics = diagnostics;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _backoffDelays = backoffDelays ?? DefaultBackoffDelays;
        _maxAttempts = _backoffDelays.Count + 1;
    }

    public static string UserAgent => DefaultUserAgent;

    public async Task<NuvioHttpResponse> GetStringAsync(NuvioHttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        AddonUrlPolicy.ValidateRemoteUri(request.Uri, $"{request.ResourceKind} URL");

        var currentUri = request.Uri;
        var redirectCount = 0;

        while (true)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(request.Timeout);

            using var releaser = await _throttler.AcquireAsync(currentUri.Host, linkedCts.Token).ConfigureAwait(false);
            var stopwatch = Stopwatch.StartNew();

            HttpResponseMessage? response = null;
            try
            {
                response = await SendWithRetryAsync(currentUri, request, linkedCts.Token).ConfigureAwait(false);

                if (RedirectPolicy.IsRedirect((int)response.StatusCode))
                {
                    if (++redirectCount > RedirectPolicy.MaxRedirects)
                    {
                        throw new NuvioValidationException(
                            $"{request.ResourceKind} exceeded the {RedirectPolicy.MaxRedirects} redirect limit.");
                    }

                    if (response.Headers.Location is not Uri location)
                    {
                        throw new NuvioValidationException(
                            $"{request.ResourceKind} redirect response missing Location header.");
                    }

                    var target = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                    AddonUrlPolicy.ValidateRemoteUri(target, $"{request.ResourceKind} redirect");
                    RedirectPolicy.Validate(currentUri, target, request.ResourceKind);

                    RecordEvent(
                        NetworkEventKind.Redirect,
                        currentUri.Host,
                        (int)response.StatusCode,
                        stopwatch.ElapsedMilliseconds,
                        request,
                        $"Redirect to host={target.Host}");

                    response.Dispose();
                    response = null;
                    currentUri = target;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var message = $"{request.ResourceKind} request failed with status {(int)response.StatusCode}.";
                    RecordEvent(
                        NetworkEventKind.Failure,
                        currentUri.Host,
                        (int)response.StatusCode,
                        stopwatch.ElapsedMilliseconds,
                        request,
                        message);
                    throw new HttpRequestException(message, null, response.StatusCode);
                }

                var body = await BoundedHttpResponseReader
                    .ReadStringAsync(response, request.MaxBytes, request.ResourceKind, linkedCts.Token)
                    .ConfigureAwait(false);

                stopwatch.Stop();
                RecordEvent(
                    NetworkEventKind.Success,
                    currentUri.Host,
                    (int)response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    request,
                    $"{request.ResourceKind} ok");

                return new NuvioHttpResponse(body, (int)response.StatusCode, currentUri);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                stopwatch.Stop();
                RecordEvent(
                    NetworkEventKind.Failure,
                    currentUri.Host,
                    null,
                    stopwatch.ElapsedMilliseconds,
                    request,
                    $"{request.ResourceKind} timed out after {request.Timeout.TotalSeconds:0.#}s");
                throw new TimeoutException(
                    $"{request.ResourceKind} request to {currentUri.Host} timed out after {request.Timeout.TotalSeconds:0.#}s.");
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Uri uri,
        NuvioHttpRequest request,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < _maxAttempts; attempt++)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, uri);
                message.Headers.UserAgent.ParseAdd(DefaultUserAgent);
                message.Headers.Accept.ParseAdd("application/json");

                var response = await _httpClient
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (ShouldRetry(response) && attempt < _backoffDelays.Count)
                {
                    var status = (int)response.StatusCode;
                    response.Dispose();
                    RecordEvent(
                        NetworkEventKind.Failure,
                        uri.Host,
                        status,
                        null,
                        request,
                        $"transient {status}; retrying");
                    await DelayAsync(_backoffDelays[attempt], cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException ex) when (attempt < _backoffDelays.Count)
            {
                lastError = ex;
                RecordEvent(
                    NetworkEventKind.Failure,
                    uri.Host,
                    null,
                    null,
                    request,
                    $"transient {ex.GetType().Name}; retrying");
                await DelayAsync(_backoffDelays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastError ?? new HttpRequestException($"{request.ResourceKind} request to {uri.Host} failed.");
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, _timeProvider, cancellationToken);

    private static bool ShouldRetry(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        return status == 429 || status >= 500;
    }

    private void RecordEvent(
        NetworkEventKind kind,
        string host,
        int? statusCode,
        long? durationMs,
        NuvioHttpRequest request,
        string message)
    {
        _diagnostics?.Record(new NetworkDiagnosticEvent(
            Timestamp: _timeProvider.GetUtcNow(),
            Kind: kind,
            Host: host,
            StatusCode: statusCode,
            DurationMs: durationMs,
            AddonId: request.AddonId,
            ResourceKind: request.ResourceKind,
            Message: message));
    }

    public static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        UseProxy = true,
        UseCookies = false
    };

    private static string BuildUserAgent()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var platform = RuntimeInformation.OSDescription.Trim();
        return $"Nuvio.Desktop/{version} (+{platform})";
    }
}
