using MonAlbumPc.Core;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Nas.Components.Shared;
using MonAlbumPc.Nas.Media;

namespace MonAlbumPc.Nas.Components.Photos;

/// <summary>A photo or video of the Photos page, with what the tiles and the viewer need.</summary>
public sealed class PhotoEntry(LibraryItem item, Album album)
{
    public LibraryItem Item { get; } = item;
    public Album Album { get; } = album;
    public string Path => Item.Path;
    public string Name { get; } = System.IO.Path.GetFileName(item.Path);
    public string Relative { get; } = AlbumMediaPath.Relative(album.DestinationRoot, item.Path);
    public bool IsVideo => Item.IsVideo;
    public string ThumbUrl => MediaEndpoints.Url("thumb", Album.Id, Relative, Item.Date.Ticks);
    public string ViewUrl => MediaEndpoints.Url("view", Album.Id, Relative, Item.Date.Ticks);
    public string OriginalUrl => MediaEndpoints.Url("original", Album.Id, Relative, Item.Date.Ticks);
    public string DownloadUrl => OriginalUrl + "&download=true";
}

/// <summary>An entry of the album or date filter, with its number of items.</summary>
public sealed record PhotoFilter(string Key, string Label, int Count, string? Color = null, Guid? AlbumId = null,
    int? Year = null, DateTime? Month = null)
{
    public bool IsYear => Year is not null;
}

/// <summary>
/// The library shown by the Photos page (as the Windows viewer): every album scanned once per visit, filtered by album and
/// by year or month, newest first.
/// </summary>
public sealed class PhotosModel
{
    public const string AllKey = "all";

    private List<PhotoEntry> _all = [];

    public IReadOnlyList<Album> Albums { get; private set; } = [];
    public bool IsEmpty => _all.Count == 0;

    /// <summary>Walks the album folders: slow on big libraries, so call it off the render thread.</summary>
    public void Load(CoreServices core)
    {
        var albums = core.Albums.List();
        _all = albums
            .SelectMany(album => PhotoLibrary.Scan(album).Select(item => new PhotoEntry(item, album)))
            .OrderByDescending(e => e.Item.Month)
            .ThenByDescending(e => e.Item.Date)
            .ToList();
        Albums = albums;
    }

    public void Remove(IEnumerable<PhotoEntry> entries)
    {
        var gone = entries.ToHashSet();
        _all.RemoveAll(gone.Contains);
    }

    public IReadOnlyList<PhotoFilter> AlbumFilters() =>
    [
        new(AllKey, "Tous les albums", _all.Count),
        .. Albums.Select(a => new PhotoFilter(a.Id.ToString("N"), a.Name, _all.Count(e => e.Album.Id == a.Id), a.Color, a.Id)),
    ];

    /// <summary>Years, each followed by its months, newest first, for the items of <paramref name="albumId"/>.</summary>
    public IReadOnlyList<PhotoFilter> DateFilters(Guid? albumId)
    {
        var dates = new List<PhotoFilter>();
        foreach (var year in InAlbum(albumId).GroupBy(e => e.Item.Month.Year).OrderByDescending(g => g.Key))
        {
            dates.Add(new PhotoFilter($"{year.Key}", $"{year.Key}", year.Count(), Year: year.Key));
            dates.AddRange(year.GroupBy(e => e.Item.Month).OrderByDescending(g => g.Key).Select(m =>
                new PhotoFilter($"{m.Key:yyyy-MM}", Formatting.Capitalize(m.Key.ToString("MMMM", Formatting.French)), m.Count(), Month: m.Key)));
        }
        return dates;
    }

    public List<PhotoEntry> View(Guid? albumId, PhotoFilter? date) => InAlbum(albumId)
        .Where(e => date is null || (date.Year is { } y ? e.Item.Month.Year == y : e.Item.Month == date.Month))
        .ToList();

    /// <summary>The same files in a new scan, by path (selection kept across a refresh).</summary>
    public IEnumerable<PhotoEntry> Find(IEnumerable<string> paths)
    {
        var wanted = paths.ToHashSet(StringComparer.Ordinal);
        return _all.Where(e => wanted.Contains(e.Path));
    }

    private IEnumerable<PhotoEntry> InAlbum(Guid? albumId) => _all.Where(e => albumId is null || e.Album.Id == albumId);
}
