using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Nuvio.Desktop.Services;

public sealed class DecodedImageMemoryCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private int _limit;
    private bool _disposed;

    public DecodedImageMemoryCache(int limit)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        _limit = limit;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public int Limit
    {
        get
        {
            lock (_gate)
            {
                return _limit;
            }
        }
    }

    public async Task<IImage> GetOrAddAsync(
        string key,
        Func<CancellationToken, Task<Stream>> openStreamAsync,
        CancellationToken cancellationToken)
    {
        if (TryGet(key, out var cached))
        {
            return cached;
        }

        await using var stream = await openStreamAsync(cancellationToken).ConfigureAwait(false);
        var bitmap = new Bitmap(stream);

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_map.TryGetValue(key, out var existing))
            {
                bitmap.Dispose();
                _order.Remove(existing);
                _order.AddFirst(existing);
                return existing.Value.Bitmap;
            }

            var node = _order.AddFirst(new Entry(key, bitmap));
            _map[key] = node;
            TrimLocked();
            return bitmap;
        }
    }

    public void SetLimit(int limit)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        lock (_gate)
        {
            _limit = limit;
            TrimLocked();
        }
    }

    public void TrimAll()
    {
        lock (_gate)
        {
            TrimAllLocked();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            TrimAllLocked();
        }
    }

    private bool TryGet(string key, out IImage image)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                image = node.Value.Bitmap;
                return true;
            }
        }

        image = default!;
        return false;
    }

    private void TrimLocked()
    {
        while (_map.Count > _limit)
        {
            var last = _order.Last;
            if (last is null)
            {
                return;
            }

            _order.RemoveLast();
            _map.Remove(last.Value.Key);
            last.Value.Bitmap.Dispose();
        }
    }

    private void TrimAllLocked()
    {
        foreach (var node in _map.Values)
        {
            node.Value.Bitmap.Dispose();
        }

        _map.Clear();
        _order.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DecodedImageMemoryCache));
        }
    }

    private sealed record Entry(string Key, Bitmap Bitmap);
}
