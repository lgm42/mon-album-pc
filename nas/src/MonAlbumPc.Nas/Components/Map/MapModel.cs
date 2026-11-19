using MonAlbumPc.Core;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Nas.Components.Photos;
using MonAlbumPc.Nas.Components.Shared;

namespace MonAlbumPc.Nas.Components.Map;

/// <summary>What the map draws: album colors, and three integers per point (see <see cref="MapModel.Encode"/>).</summary>
public sealed record MapPoints(int Generation, IReadOnlyList<string> Colors, int[] Points);

/// <summary>
/// The located photos of the Map page (from the media index, newest first), filtered by album and by year or month
/// with the same filters as the Photos page. A point's id is its index in <see cref="Points"/>.
/// </summary>
public sealed class MapModel
{
    private List<LocatedMedia> _all = [];
    private Dictionary<Guid, Album> _albums = [];
    private PhotoEntry?[] _entries = [];

    public IReadOnlyList<Album> Albums { get; private set; } = [];
    public IReadOnlyList<LocatedMedia> Points { get; private set; } = [];
    public bool IsEmpty => _all.Count == 0;

    /// <summary>Reads the index (a query, no file is opened); call it off the render thread all the same.</summary>
    public void Load(CoreServices core)
    {
        Albums = core.Albums.List();
        _albums = Albums.ToDictionary(a => a.Id);
        _all = core.Media.ListLocated().Where(m => _albums.ContainsKey(m.AlbumId)).ToList();
    }

    public IReadOnlyList<PhotoFilter> AlbumFilters() =>
    [
        new(PhotosModel.AllKey, "Tous les albums", _all.Count),
        .. Albums.Select(a => new PhotoFilter(a.Id.ToString("N"), a.Name, _all.Count(m => m.AlbumId == a.Id), a.Color, a.Id)),
    ];

    /// <summary>Years, each followed by its months, newest first, for the points of <paramref name="albumId"/>.</summary>
    public IReadOnlyList<PhotoFilter> DateFilters(Guid? albumId)
    {
        var dates = new List<PhotoFilter>();
        foreach (var year in InAlbum(albumId).GroupBy(m => m.TakenAt.Year).OrderByDescending(g => g.Key))
        {
            dates.Add(new PhotoFilter($"{year.Key}", $"{year.Key}", year.Count(), Year: year.Key));
            dates.AddRange(year.GroupBy(m => MonthOf(m)).OrderByDescending(g => g.Key).Select(m =>
                new PhotoFilter($"{m.Key:yyyy-MM}", Formatting.Capitalize(m.Key.ToString("MMMM", Formatting.French)), m.Count(), Month: m.Key)));
        }
        return dates;
    }

    /// <summary>Keeps the points of the album and period; the ids change.</summary>
    public void Filter(Guid? albumId, PhotoFilter? date)
    {
        Points = InAlbum(albumId)
            .Where(m => date is null || (date.Year is { } y ? m.TakenAt.Year == y : MonthOf(m) == date.Month))
            .ToList();
        _entries = new PhotoEntry?[Points.Count];
    }

    /// <summary>The ids of the points inside <paramref name="bounds"/> (all of them without bounds), newest first.</summary>
    public List<int> Inside(GeoBounds? bounds)
    {
        var ids = new List<int>();
        for (var i = 0; i < Points.Count; i++)
        {
            if (bounds is not { } b || b.Contains(Points[i].Latitude, Points[i].Longitude))
            {
                ids.Add(i);
            }
        }
        return ids;
    }

    /// <summary>The tile and viewer entry of point <paramref name="id"/>, made once: the grid keys its tiles by entry.</summary>
    public PhotoEntry Entry(int id)
    {
        if (_entries[id] is { } entry)
        {
            return entry;
        }
        var point = Points[id];
        var item = new LibraryItem(point.Path, point.AlbumId, MonthOf(point), point.TakenAt, point.IsVideo, point.Size,
            _albums[point.AlbumId].DestinationRoot);
        return _entries[id] = new PhotoEntry(item, _albums[point.AlbumId]);
    }

    /// <summary>
    /// The points as the page script reads them, three integers each: latitude and longitude in hundred-thousandths of a
    /// degree (about a metre), then album color index × 2 + 1 for a video. No path or name: the script asks for those
    /// of the point under the pointer only.
    /// </summary>
    public MapPoints Encode(int generation)
    {
        var colorIndex = Albums.Select((a, i) => (a.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var points = new int[Points.Count * 3];
        for (var i = 0; i < Points.Count; i++)
        {
            var point = Points[i];
            points[3 * i] = (int)Math.Round(point.Latitude * 1e5);
            points[3 * i + 1] = (int)Math.Round(point.Longitude * 1e5);
            points[3 * i + 2] = colorIndex[point.AlbumId] * 2 + (point.IsVideo ? 1 : 0);
        }
        return new MapPoints(generation, Albums.Select(a => a.Color).ToList(), points);
    }

    private IEnumerable<LocatedMedia> InAlbum(Guid? albumId) => _all.Where(m => albumId is null || m.AlbumId == albumId);

    private static DateTime MonthOf(LocatedMedia media) => new(media.TakenAt.Year, media.TakenAt.Month, 1);
}
