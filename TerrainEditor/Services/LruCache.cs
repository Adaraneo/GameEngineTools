namespace TerrainEditor.Services;

/// <summary>
/// Minimal fixed-capacity least-recently-used cache — once <see cref="_capacity"/> entries are
/// held, adding a new one evicts whichever was accessed longest ago. Not thread-safe (TerrainEditor
/// is single-threaded/UI-driven; nothing here is touched off the UI thread).
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new(); // front = most recently used

    public LruCache(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    public int Count => _map.Count;

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Inserts or updates an entry, marking it most-recently-used — evicts the least-
    /// recently-used entry first if this would exceed capacity (and the key is new).</summary>
    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
        }
        else if (_map.Count >= _capacity)
        {
            var lru = _order.Last;
            if (lru is not null)
            {
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
            }
        }

        var node = new LinkedListNode<(TKey, TValue)>((key, value));
        _order.AddFirst(node);
        _map[key] = node;
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }
}
