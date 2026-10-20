using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Nas.Components.Shared;

namespace MonAlbumPc.Nas.Components.Photos;

/// <summary>
/// State of the viewer, independent of the UI: the scanned library, the album/year/month filters, the visible items
/// and the selection. Same rules as the Windows PhotosPage.
/// </summary>
public sealed class PhotoBrowser
{
    private readonly Dictionary<string, long> _versions = [];
    private List<LibraryItem> _all = [];

    public IReadOnlyList<Album> Albums { get; private set; } = [];
    public IReadOnlyList<LibraryItem> Visible { get; private set; } = [];
    public IReadOnlyList<FilterEntry> AlbumEntries { get; private set; } = [];
    public IReadOnlyList<FilterEntry> DateEntries { get; private set; } = [];
    public Guid? AlbumFilter { get; private set; }
    public int? YearFilter { get; private set; }
    public DateTime? MonthFilter { get; private set; }
    public LibraryItem? Selected { get; private set; }
    public bool IsEmpty => _all.Count == 0;

    public int SelectedIndex => Selected is null ? -1 : IndexOf(Selected.Path);

    /// <summary>Walks every album folder. Slow on big libraries: call it off the render thread.</summary>
    public static List<LibraryItem> Scan(IEnumerable<Album> albums) => albums.SelectMany(PhotoLibrary.Scan).ToList();

    public void Load(IReadOnlyList<Album> albums, List<LibraryItem> items)
    {
        Albums = albums;
        _all = items;
        if (AlbumFilter is { } id && albums.All(a => a.Id != id))
        {
            AlbumFilter = null;
        }
        Rebuild();
    }

    public void FilterAlbum(Guid? album)
    {
        AlbumFilter = album;
        YearFilter = null;
        MonthFilter = null;
        Rebuild();
    }

    public void FilterDate(FilterEntry entry)
    {
        YearFilter = entry.IsYear ? entry.Month!.Value.Year : null;
        MonthFilter = entry.IsYear ? null : entry.Month;
        Rebuild();
    }

    public void ClearDate()
    {
        YearFilter = null;
        MonthFilter = null;
        Rebuild();
    }

    public bool IsDateSelected(FilterEntry entry) => entry.IsYear
        ? YearFilter == entry.Month?.Year && MonthFilter is null
        : MonthFilter == entry.Month;

    public void Select(LibraryItem? item) => Selected = item;

    /// <summary>Moves the selection; returns false at either end.</summary>
    public bool Move(int delta)
    {
        var index = SelectedIndex + delta;
        if (Selected is null || index < 0 || index >= Visible.Count)
        {
            return false;
        }
        Selected = Visible[index];
        return true;
    }

    /// <summary>"Marie · Octobre 2026 · 16 éléments".</summary>
    public string Caption
    {
        get
        {
            var album = Albums.FirstOrDefault(a => a.Id == AlbumFilter)?.Name ?? "Tous les albums";
            var period = MonthFilter is { } m ? " · " + Formatting.Month(m) : YearFilter is { } y ? $" · {y}" : "";
            return $"{album}{period} · {Formatting.Plural(Visible.Count, "élément", "éléments")}";
        }
    }

    public string? AlbumName(Guid id) => Albums.FirstOrDefault(a => a.Id == id)?.Name;

    /// <summary>Changes after a rotation, so the browser loads the new thumbnail and file.</summary>
    public long Version(string path) => _versions.GetValueOrDefault(path);

    public void Touch(string path) => _versions[path] = DateTime.UtcNow.Ticks;

    /// <summary>Removes a deleted item and selects its neighbour.</summary>
    public void Remove(LibraryItem item)
    {
        var index = IndexOf(item.Path);
        _all.RemoveAll(i => i.Path == item.Path);
        Rebuild();
        Selected = Visible.Count == 0 ? null : Visible[Math.Clamp(index, 0, Visible.Count - 1)];
    }

    private int IndexOf(string path)
    {
        for (var i = 0; i < Visible.Count; i++)
        {
            if (Visible[i].Path == path)
            {
                return i;
            }
        }
        return -1;
    }

    private void Rebuild()
    {
        AlbumEntries =
        [
            new FilterEntry("Tous les albums", _all.Count, null, null, null, false),
            .. Albums.Select(a => new FilterEntry(a.Name, _all.Count(i => i.AlbumId == a.Id), a.Color, a.Id, null, false)),
        ];

        var inAlbum = _all.Where(i => AlbumFilter is null || i.AlbumId == AlbumFilter).ToList();
        var dates = new List<FilterEntry>();
        foreach (var year in inAlbum.GroupBy(i => i.Month.Year).OrderByDescending(g => g.Key))
        {
            dates.Add(new FilterEntry(year.Key.ToString(), year.Count(), null, null, new DateTime(year.Key, 1, 1), true));
            dates.AddRange(year.GroupBy(i => i.Month).OrderByDescending(g => g.Key).Select(m =>
                new FilterEntry(Formatting.Capitalize(m.Key.ToString("MMMM", Formatting.French)), m.Count(), null, null, m.Key, false)));
        }
        DateEntries = dates;
        if (YearFilter is { } y && dates.All(d => !d.IsYear || d.Month!.Value.Year != y)
            || MonthFilter is { } m && dates.All(d => d.IsYear || d.Month != m))
        {
            YearFilter = null;
            MonthFilter = null;
        }

        Visible = inAlbum
            .Where(i => MonthFilter is null || i.Month == MonthFilter)
            .Where(i => YearFilter is null || i.Month.Year == YearFilter)
            .OrderByDescending(i => i.Month)
            .ThenByDescending(i => i.Date)
            .ToList();
        if (Selected is not null && IndexOf(Selected.Path) < 0)
        {
            Selected = null;
        }
    }
}

/// <summary>An entry of the left filter pane.</summary>
public sealed record FilterEntry(string Label, int Count, string? Color, Guid? AlbumId, DateTime? Month, bool IsYear);
