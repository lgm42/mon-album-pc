using MonAlbumPc.Core;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Tags;
using MonAlbumPc.Nas.Components.Shared;
using MonAlbumPc.Nas.Components.Tags;
using MonAlbumPc.Nas.Media;

namespace MonAlbumPc.Nas.Components.Photos;

/// <summary>
/// A photo or video of the Photos page, with what the tiles and the viewer need. <see cref="Source"/> is its provenance,
/// when the page read it with the others (the map does not).
/// </summary>
public sealed class PhotoEntry(LibraryItem item, Album album, MediaSource? source = null)
{
    public LibraryItem Item { get; } = item;
    public Album Album { get; } = album;
    public MediaSource? Source { get; } = source;
    public string Path => Item.Path;
    public string Name { get; } = System.IO.Path.GetFileName(item.Path);
    public string Relative { get; } = AlbumMediaPath.Relative(album.DestinationRoot, item.Path);
    public bool IsVideo => Item.IsVideo;
    public string ThumbUrl => MediaEndpoints.Url("thumb", Album.Id, Relative, Item.Date.Ticks);
    public string ViewUrl => MediaEndpoints.Url("view", Album.Id, Relative, Item.Date.Ticks);
    public string OriginalUrl => MediaEndpoints.Url("original", Album.Id, Relative, Item.Date.Ticks);
    public string DownloadUrl => OriginalUrl + "&download=true";

    /// <summary>Its user tags, as indexed (updated by the page as soon as it tags).</summary>
    public IReadOnlyList<string> Tags { get; set; } = [];
}

/// <summary>An entry of the album, provenance or date filter, with its number of items.</summary>
public sealed record PhotoFilter(string Key, string Label, int Count, string? Color = null, Guid? AlbumId = null,
    int? Year = null, DateTime? Month = null, MediaSourceKind? Source = null)
{
    public bool IsYear => Year is not null;
}

/// <summary>
/// The library shown by the Photos page (as the Windows viewer): every album scanned once per visit, filtered by album,
/// provenance and year or month, newest first.
/// </summary>
public sealed class PhotosModel
{
    public const string AllKey = "all";
    public const string AllSourcesKey = "all-sources";

    private List<PhotoEntry> _all = [];

    public IReadOnlyList<Album> Albums { get; private set; } = [];
    public bool IsEmpty => _all.Count == 0;

    /// <summary>
    /// Walks the album folders and reads where each file comes from (once per album, from the catalog and the journals):
    /// slow on big libraries, so call it off the render thread.
    /// </summary>
    public void Load(CoreServices core)
    {
        var albums = core.Albums.List();
        _all = albums
            .SelectMany(album =>
            {
                var provenance = AlbumProvenance.Load(core.History, album);
                return PhotoLibrary.Scan(album).Select(item => new PhotoEntry(item, album, provenance.Of(item.Path)));
            })
            .OrderByDescending(e => e.Item.Month)
            .ThenByDescending(e => e.Item.Date)
            .ToList();
        Albums = albums;
        LoadTags(core);
    }

    /// <summary>Every tag of the library (those without photos too), sorted as a tree.</summary>
    public IReadOnlyList<TagCount> AllTags { get; private set; } = [];

    /// <summary>Takes the tags from the index again (a query: no file is opened), after tagging or news from another server.</summary>
    public void LoadTags(CoreServices core)
    {
        var byFile = core.Tags.ByFile();
        foreach (var entry in _all)
        {
            entry.Tags = byFile.GetValueOrDefault((entry.Album.Id, entry.Relative)) ?? [];
        }
        AllTags = core.Tags.List();
    }

    /// <summary>The tags of the items of the album, period and provenance, with "Sans tag".</summary>
    public (List<TagFilterEntry> Tags, int Untagged) TagFilters(Guid? albumId, PhotoFilter? date, MediaSourceKind? source)
    {
        var items = InAlbum(albumId).Where(e => InPeriod(e, date) && (source is null || e.Source?.Kind == source)).ToList();
        return (TagSelection.Entries(AllTags.Select(t => t.Name), items.Select(e => e.Tags)), items.Count(e => e.Tags.Count == 0));
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

    /// <summary>Toutes, Appareil photo, WhatsApp, Captures d'écran, Autres, for the items of the album and period.</summary>
    public IReadOnlyList<PhotoFilter> SourceFilters(Guid? albumId, PhotoFilter? date, TagSelection? tags = null)
    {
        var items = InAlbum(albumId).Where(e => InPeriod(e, date) && (tags?.Matches(e.Tags) ?? true)).ToList();
        return
        [
            new(AllSourcesKey, "Toutes", items.Count),
            .. Enum.GetValues<MediaSourceKind>().Select(kind => new PhotoFilter(
                $"source-{kind}".ToLowerInvariant(), MediaSource.KindLabel(kind), items.Count(e => e.Source?.Kind == kind), Source: kind)),
        ];
    }

    /// <summary>Years, each followed by its months, newest first, for the items of <paramref name="albumId"/>.</summary>
    public IReadOnlyList<PhotoFilter> DateFilters(Guid? albumId, MediaSourceKind? source = null, TagSelection? tags = null)
    {
        var dates = new List<PhotoFilter>();
        foreach (var year in InAlbum(albumId).Where(e => (source is null || e.Source?.Kind == source) && (tags?.Matches(e.Tags) ?? true))
                     .GroupBy(e => e.Item.Month.Year).OrderByDescending(g => g.Key))
        {
            dates.Add(new PhotoFilter($"{year.Key}", $"{year.Key}", year.Count(), Year: year.Key));
            dates.AddRange(year.GroupBy(e => e.Item.Month).OrderByDescending(g => g.Key).Select(m =>
                new PhotoFilter($"{m.Key:yyyy-MM}", Formatting.Capitalize(m.Key.ToString("MMMM", Formatting.French)), m.Count(), Month: m.Key)));
        }
        return dates;
    }

    public List<PhotoEntry> View(Guid? albumId, PhotoFilter? date, MediaSourceKind? source = null, TagSelection? tags = null) => InAlbum(albumId)
        .Where(e => InPeriod(e, date) && (source is null || e.Source?.Kind == source) && (tags?.Matches(e.Tags) ?? true))
        .ToList();

    private static bool InPeriod(PhotoEntry entry, PhotoFilter? date) =>
        date is null || (date.Year is { } y ? entry.Item.Month.Year == y : entry.Item.Month == date.Month);

    /// <summary>The same files in a new scan, by path (selection kept across a refresh).</summary>
    public IEnumerable<PhotoEntry> Find(IEnumerable<string> paths)
    {
        var wanted = paths.ToHashSet(StringComparer.Ordinal);
        return _all.Where(e => wanted.Contains(e.Path));
    }

    private IEnumerable<PhotoEntry> InAlbum(Guid? albumId) => _all.Where(e => albumId is null || e.Album.Id == albumId);
}
