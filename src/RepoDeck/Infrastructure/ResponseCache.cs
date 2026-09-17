using System.Collections.Concurrent;

namespace RepoDeck.Infrastructure;

/// <summary>
/// In-memory, time-limited cache of GitHub responses.
/// GitHub's unauthenticated limits are tight (60 core requests an hour), so revisiting
/// a repository page must not cost another round trip.
/// </summary>
public sealed class ResponseCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private readonly int _maxEntries;

    public ResponseCache(TimeProvider? timeProvider = null, int maxEntries = 256)
    {
        _time = timeProvider ?? TimeProvider.System;
        _maxEntries = maxEntries;
    }

    public bool TryGet<T>(string key, out T value) where T : class
    {
        value = default!;
        if (!_entries.TryGetValue(key, out var entry)) return false;

        if (entry.ExpiresAt <= _time.GetUtcNow())
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        if (entry.Value is not T typed) return false;

        value = typed;
        return true;
    }

    public void Set<T>(string key, T value, TimeSpan lifetime) where T : class
    {
        if (_entries.Count >= _maxEntries) EvictExpiredOrOldest();
        _entries[key] = new Entry(value, _time.GetUtcNow() + lifetime);
    }

    public void Clear() => _entries.Clear();

    public int Count => _entries.Count;

    private void EvictExpiredOrOldest()
    {
        var now = _time.GetUtcNow();
        var expired = _entries.Where(kv => kv.Value.ExpiresAt <= now).Select(kv => kv.Key).ToList();
        foreach (var key in expired) _entries.TryRemove(key, out _);

        if (_entries.Count < _maxEntries) return;

        // Still full: drop whatever expires soonest.
        var oldest = _entries.OrderBy(kv => kv.Value.ExpiresAt).FirstOrDefault();
        if (oldest.Key is not null) _entries.TryRemove(oldest.Key, out _);
    }

    private readonly record struct Entry(object Value, DateTimeOffset ExpiresAt);
}
