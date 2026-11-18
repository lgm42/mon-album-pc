namespace MonAlbumPc.Core.Media;

/// <summary>
/// Selection of items shown in a given order, as in the Windows Explorer: a click selects one item, Ctrl+click adds
/// or removes it, Shift+click selects from the anchor (the last item clicked without Shift) to the clicked one.
/// </summary>
public sealed class Selection<T> where T : class
{
    private readonly HashSet<T> _items = [];

    public IReadOnlyCollection<T> Items => _items;

    public int Count => _items.Count;

    /// <summary>Item with the focus: the last one clicked or reached with the arrow keys, selected or not.</summary>
    public T? Current { get; private set; }

    /// <summary>Fixed end of the Shift ranges.</summary>
    public T? Anchor { get; private set; }

    public bool Contains(T item) => _items.Contains(item);

    /// <summary>
    /// Click on <paramref name="item"/> in <paramref name="order"/>. <paramref name="toggle"/> is Ctrl,
    /// <paramref name="range"/> is Shift; with both, the range is added to what is already selected.
    /// </summary>
    public void Click(IReadOnlyList<T> order, T item, bool toggle, bool range)
    {
        var to = IndexOf(order, item);
        var from = range && Anchor is not null ? IndexOf(order, Anchor) : -1;
        if (to >= 0 && from >= 0)
        {
            if (!toggle)
            {
                _items.Clear();
            }
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
            {
                _items.Add(order[i]);
            }
        }
        else if (toggle)
        {
            if (!_items.Remove(item))
            {
                _items.Add(item);
            }
            Anchor = item;
        }
        else
        {
            Select(item);
        }
        Current = item;
    }

    /// <summary>Selects only <paramref name="item"/>, or nothing.</summary>
    public void Select(T? item)
    {
        _items.Clear();
        if (item is not null)
        {
            _items.Add(item);
        }
        Current = Anchor = item;
    }

    public void SelectAll(IEnumerable<T> order) => _items.UnionWith(order);

    /// <summary>Forgets the items that are no longer shown, so that nothing hidden is acted upon.</summary>
    public void Retain(Func<T, bool> shown)
    {
        _items.RemoveWhere(i => !shown(i));
        if (Current is not null && !shown(Current))
        {
            Current = null;
        }
        if (Anchor is not null && !shown(Anchor))
        {
            Anchor = null;
        }
    }

    private static int IndexOf(IReadOnlyList<T> order, T item)
    {
        for (var i = 0; i < order.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(order[i], item))
            {
                return i;
            }
        }
        return -1;
    }
}
