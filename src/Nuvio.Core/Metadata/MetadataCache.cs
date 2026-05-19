using Nuvio.Core.Models;

namespace Nuvio.Core.Metadata;

public sealed class MetadataCache
{
    public static TimeSpan DefaultTtl { get; } = TimeSpan.FromMinutes(30);
    public const int DefaultCapacity = 256;

    private readonly TimeSpan _ttl;
    private readonly int _capacity;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly LruMap<MediaDetails> _details;
    private readonly LruMap<IReadOnlyList<SubtitleTrack>> _subtitles;

    public MetadataCache(TimeSpan? ttl = null, TimeProvider? timeProvider = null, int? capacity = null)
    {
        _ttl = ttl ?? DefaultTtl;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _capacity = capacity ?? DefaultCapacity;
        if (_capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _details = new LruMap<MediaDetails>(_capacity);
        _subtitles = new LruMap<IReadOnlyList<SubtitleTrack>>(_capacity);
    }

    public bool TryGetDetails(string type, string id, out MediaDetails details)
    {
        var key = Key(type, id);
        lock (_gate)
        {
            if (_details.TryGet(key, _timeProvider.GetUtcNow(), _ttl, out details!))
            {
                return true;
            }
        }

        details = default!;
        return false;
    }

    public void SetDetails(string type, string id, MediaDetails details)
    {
        var key = Key(type, id);
        lock (_gate)
        {
            _details.Set(key, details, _timeProvider.GetUtcNow());
        }
    }

    public bool TryGetSubtitles(string type, string id, out IReadOnlyList<SubtitleTrack> subtitles)
    {
        var key = Key(type, id);
        lock (_gate)
        {
            if (_subtitles.TryGet(key, _timeProvider.GetUtcNow(), _ttl, out var stored))
            {
                subtitles = stored!;
                return true;
            }
        }

        subtitles = Array.Empty<SubtitleTrack>();
        return false;
    }

    public void SetSubtitles(string type, string id, IReadOnlyList<SubtitleTrack> subtitles)
    {
        var key = Key(type, id);
        lock (_gate)
        {
            _subtitles.Set(key, subtitles, _timeProvider.GetUtcNow());
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _details.Clear();
            _subtitles.Clear();
        }
    }

    private static string Key(string type, string id) => $"{type}:{id}";

    private sealed class LruMap<T>
    {
        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<Entry>> _map;
        private readonly LinkedList<Entry> _order = new();

        public LruMap(int capacity)
        {
            _capacity = capacity;
            _map = new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
        }

        public bool TryGet(string key, DateTimeOffset now, TimeSpan ttl, out T? value)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }

            if (now - node.Value.StoredAt > ttl)
            {
                _order.Remove(node);
                _map.Remove(key);
                value = default;
                return false;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        public void Set(string key, T value, DateTimeOffset now)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = _order.AddFirst(new Entry(key, value, now));
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var oldest = _order.Last!;
                _order.RemoveLast();
                _map.Remove(oldest.Value.Key);
            }
        }

        public void Clear()
        {
            _order.Clear();
            _map.Clear();
        }

        private readonly record struct Entry(string Key, T Value, DateTimeOffset StoredAt);
    }
}
