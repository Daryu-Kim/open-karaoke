using OpenKaraoke.Core.Search;

namespace OpenKaraoke.Core.Caching;

/// <summary>
/// Thread-safe in-memory search cache with TTL and max-entry eviction (least recently
/// used). Keeps YouTube API quota usage low for repeated queries.
/// </summary>
public sealed class SearchCache
{
    private sealed class Entry
    {
        public required SearchVideoItem[] Items;
        public DateTime ExpiresAtUtc;
        public DateTime LastAccessUtc;
    }

    private readonly object _gate = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public SearchCache(TimeSpan ttl, int maxEntries)
    {
        _ttl = ttl;
        _maxEntries = Math.Max(1, maxEntries);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public IReadOnlyList<SearchVideoItem>? Get(string query, DateTime utcNow)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(query, out Entry? entry))
            {
                return null;
            }

            if (entry.ExpiresAtUtc <= utcNow)
            {
                _entries.Remove(query);
                return null;
            }

            entry.LastAccessUtc = utcNow;
            return entry.Items;
        }
    }

    public void Set(string query, IReadOnlyList<SearchVideoItem> items, DateTime utcNow)
    {
        lock (_gate)
        {
            PruneLocked(utcNow);

            _entries[query] = new Entry
            {
                Items = items.ToArray(),
                ExpiresAtUtc = utcNow + _ttl,
                LastAccessUtc = utcNow,
            };
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void PruneLocked(DateTime utcNow)
    {
        if (_entries.Count < _maxEntries)
        {
            return;
        }

        foreach (KeyValuePair<string, Entry> kvp in _entries.Where(e => e.Value.ExpiresAtUtc <= utcNow).ToList())
        {
            _entries.Remove(kvp.Key);
        }

        if (_entries.Count < _maxEntries)
        {
            return;
        }

        foreach (KeyValuePair<string, Entry> kvp in _entries
                     .OrderBy(e => e.Value.LastAccessUtc)
                     .Take(_entries.Count - _maxEntries + 1)
                     .ToList())
        {
            _entries.Remove(kvp.Key);
        }
    }
}
