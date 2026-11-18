using MonAlbumPc.Nas.Components.Shared;

namespace MonAlbumPc.Nas.Components.Photos;

/// <summary>A month title, or a row of tiles (<see cref="Start"/>..<see cref="Start"/>+<see cref="Length"/> in the view).</summary>
public sealed record PhotoRow(int Index, double Top, double Height, string? Title, string? Count, int Start, int Length)
{
    public bool IsTitle => Title is not null;
}

/// <summary>
/// Rows of the photo grid, with their exact position: the page renders only the rows near the visible area, so that
/// tens of thousands of photos scroll as fast as a hundred.
/// </summary>
public sealed class PhotoGridLayout
{
    public const double Gap = 4;
    public const double TitleHeight = 52;

    private readonly List<PhotoRow> _rows = [];
    private readonly int[] _rowOfItem;

    public PhotoGridLayout(IReadOnlyList<PhotoEntry> view, double width, double tileSize)
    {
        _rowOfItem = new int[view.Count];
        Columns = Math.Max(1, (int)((width + Gap) / (tileSize + Gap)));
        Tile = Math.Max(1, Math.Floor((width - Gap * (Columns - 1)) / Columns * 100) / 100);
        var top = 0.0;
        var start = 0;
        while (start < view.Count)
        {
            var month = view[start].Item.Month;
            var end = start;
            while (end < view.Count && view[end].Item.Month == month)
            {
                end++;
            }
            _rows.Add(new PhotoRow(_rows.Count, top, TitleHeight, Formatting.Month(month),
                Formatting.Plural(end - start, "élément", "éléments"), start, 0));
            top += TitleHeight;
            for (var i = start; i < end; i += Columns)
            {
                var row = new PhotoRow(_rows.Count, top, Tile + Gap, null, null, i, Math.Min(Columns, end - i));
                Array.Fill(_rowOfItem, row.Index, i, row.Length);
                _rows.Add(row);
                top += row.Height;
            }
            start = end;
        }
        Height = top;
    }

    public int Columns { get; }
    public double Tile { get; }
    public double Height { get; }
    public IReadOnlyList<PhotoRow> Rows => _rows;

    /// <summary>Rows overlapping <paramref name="top"/>..<paramref name="bottom"/>.</summary>
    public IEnumerable<PhotoRow> Between(double top, double bottom)
    {
        var first = FirstAtOrBelow(top);
        for (var i = first; i < _rows.Count && _rows[i].Top < bottom; i++)
        {
            yield return _rows[i];
        }
    }

    /// <summary>The row holding item <paramref name="index"/> of the view.</summary>
    public PhotoRow? RowOf(int index) => index >= 0 && index < _rowOfItem.Length ? _rows[_rowOfItem[index]] : null;

    /// <summary>Scroll position showing item <paramref name="index"/> at the top, with its month title when it starts the month.</summary>
    public double ScrollTopFor(int index)
    {
        if (RowOf(index) is not { } row)
        {
            return 0;
        }
        return row.Index > 0 && _rows[row.Index - 1].IsTitle ? _rows[row.Index - 1].Top : row.Top;
    }

    /// <summary>Index of the first item of the row at <paramref name="top"/>, to keep it in sight when the tiles resize.</summary>
    public int? ItemAt(double top)
    {
        var row = FirstAtOrBelow(top);
        for (var i = row; i < _rows.Count; i++)
        {
            if (!_rows[i].IsTitle)
            {
                return _rows[i].Start;
            }
        }
        return null;
    }

    private int FirstAtOrBelow(double top)
    {
        int low = 0, high = _rows.Count - 1, found = _rows.Count;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_rows[mid].Top + _rows[mid].Height > top)
            {
                found = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }
        return found;
    }
}
