using System.Collections.Concurrent;

namespace Nuvio.Core.Net;

public sealed class PerHostThrottler : IDisposable
{
    public const int DefaultMaxConcurrentPerHost = 4;

    private readonly int _maxConcurrentPerHost;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores =
        new(StringComparer.OrdinalIgnoreCase);
    private int _disposed;

    public PerHostThrottler(int maxConcurrentPerHost = DefaultMaxConcurrentPerHost)
    {
        if (maxConcurrentPerHost < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentPerHost));
        }

        _maxConcurrentPerHost = maxConcurrentPerHost;
    }

    public int MaxConcurrentPerHost => _maxConcurrentPerHost;

    public async Task<IDisposable> AcquireAsync(string host, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Host is required.", nameof(host));
        }

        var key = host.ToLowerInvariant();
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(_maxConcurrentPerHost, _maxConcurrentPerHost));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    public int InFlight(string host)
    {
        var key = host.ToLowerInvariant();
        if (!_semaphores.TryGetValue(key, out var semaphore))
        {
            return 0;
        }

        return _maxConcurrentPerHost - semaphore.CurrentCount;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        foreach (var semaphore in _semaphores.Values)
        {
            semaphore.Dispose();
        }

        _semaphores.Clear();
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            var semaphore = Interlocked.Exchange(ref _semaphore, null);
            semaphore?.Release();
        }
    }
}
