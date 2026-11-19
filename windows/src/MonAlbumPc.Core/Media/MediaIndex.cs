using System.Globalization;
using Microsoft.Data.Sqlite;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;
using MonAlbumPc.Core.Tags;

namespace MonAlbumPc.Core.Media;

/// <summary>A photo or video of the catalog with its shooting place.</summary>
public sealed record LocatedMedia(Guid AlbumId, string Path, DateTime TakenAt, double Latitude, double Longitude, bool IsVideo, long Size);

/// <summary>
/// Where a refresh stands: <paramref name="Done"/> files read out of <paramref name="Total"/> new or changed ones, the last
/// being <paramref name="Item"/>. While the folder is still being listed, <paramref name="Total"/> is 0 and
/// <paramref name="Done"/> counts the files found so far.
/// </summary>
public readonly record struct MediaIndexProgress(string AlbumName, int Done, int Total, string? Item = null);

/// <summary>
/// What a refresh changed in the catalog, out of <paramref name="Files"/> media files in the album folder;
/// <paramref name="Tagged"/> files got their tags from the journals of other servers.
/// </summary>
public readonly record struct MediaIndexResult(int Read, int Removed, int Files = 0, int Tagged = 0)
{
    public bool Changed => Read > 0 || Removed > 0 || Tagged > 0;
}

/// <summary>
/// Metadata of every media file found in the album folders (shooting date, GPS position, user tags), cached in the
/// <c>media</c> and <c>media_tags</c> tables so that the map and the viewer do not open each file again. Files are read
/// again only when their size, modification date or tag sidecar changes. What the album journals of other servers already
/// say is taken from them: opening thousands of files over a network share is slow. Tags written by another server
/// without changing the size of the file come from the tag journals (<see cref="TagJournals"/>).
/// </summary>
public sealed class MediaIndex(Database database, AlbumJournals? journals = null, ImportHistory? history = null, TagJournals? tagJournals = null)
{
    private const string WallClock = "yyyy-MM-dd'T'HH:mm:ss";
    private const int BatchSize = 100;

    // A big folder on a network share takes a while to list: tell how far it got now and then.
    private const int ListingReportEvery = 500;

    /// <summary>Brings the index of <paramref name="album"/> up to date with its folder.</summary>
    public MediaIndexResult Refresh(Album album, IProgress<MediaIndexProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(album.DestinationRoot))
        {
            // Disk unplugged or network share gone: keep what we know.
            return default;
        }
        var known = Known(album.Id);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toRead = new List<FileInfo>();
        var (files, sidecars) = Walk(album.DestinationRoot);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Relative(album.DestinationRoot, file.FullName);
            seen.Add(relative);
            if (seen.Count % ListingReportEvery == 0)
            {
                progress?.Report(new MediaIndexProgress(album.Name, seen.Count, 0, file.Name));
            }
            if (!known.TryGetValue(relative, out var state) || state.Size != file.Length || state.ModifiedAt != Modified(file)
                || !state.TagsRead || state.Sidecar != SidecarOf(file.FullName, sidecars))
            {
                toRead.Add(file);
            }
        }

        // Tags may be known of files never indexed (tagged right after their album was added): those gone go too.
        var removed = known.Keys.Concat(TaggedPaths(album.Id)).Where(path => !seen.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var chunk in removed.Chunk(BatchSize))
        {
            Write(connection =>
            {
                foreach (var path in chunk)
                {
                    connection.Execute("DELETE FROM media WHERE album_id = $album AND path = $path",
                        ("$album", album.Id.ToString()), ("$path", path));
                    connection.Execute("DELETE FROM media_tags WHERE album_id = $album AND path = $path",
                        ("$album", album.Id.ToString()), ("$path", path));
                }
            });
        }

        var done = 0;
        AlbumProvenance? provenance = null;
        progress?.Report(new MediaIndexProgress(album.Name, 0, toRead.Count));
        foreach (var chunk in toRead.Chunk(BatchSize / 4))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<(FileInfo File, MediaInfo Info, List<string> Tags, string? Sidecar)>();
            foreach (var file in chunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    file.Refresh();
                    if (!file.Exists)
                    {
                        continue;
                    }
                    var relative = Relative(album.DestinationRoot, file.FullName);
                    // Only the tags to read again (first refresh with tags, sidecar changed): the rest is still valid.
                    var info = known.TryGetValue(relative, out var state) && state.Size == file.Length && state.ModifiedAt == Modified(file)
                        ? state.Info
                        : FromJournals(album.Id, relative, file.Length) ?? MediaMetadata.Read(file.FullName);
                    var sidecar = SidecarOf(file.FullName, sidecars);
                    List<string> tags = [];
                    if (sidecar is not null || !IsAsImported(album.Id, relative, file.Length))
                    {
                        provenance ??= history is null ? null : AlbumProvenance.Load(history, album);
                        var keyword = (provenance?.Of(file.FullName) ?? MediaSource.Classify(null, file.FullName)).Keyword;
                        tags = UserTags.Read(file.FullName, keyword);
                    }
                    entries.Add((file, info, tags, sidecar));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Read again at the next refresh.
                }
            }
            Write(connection =>
            {
                foreach (var (file, info, tags, sidecar) in entries)
                {
                    var taken = info.TakenAt ?? TakenDateResolver.ParseFileName(file.Name) ?? Truncate(file.LastWriteTime);
                    var relative = Relative(album.DestinationRoot, file.FullName);
                    Upsert(connection, album.Id, relative, file, taken, info.Location, sidecar);
                    TagCatalog.Store(connection, album.Id, relative, tags);
                }
            });
            done += chunk.Length;
            progress?.Report(new MediaIndexProgress(album.Name, done, toRead.Count, chunk[^1].Name));
        }
        var tagged = tagJournals?.Sync(album) ?? 0;
        return new MediaIndexResult(toRead.Count, removed.Count, seen.Count, tagged);
    }

    /// <summary>
    /// True when another server journaled this very file at import, with this size: it then has no user tag yet (tagging
    /// it would have changed its size, or given it a sidecar), and it needs not be opened over the network.
    /// </summary>
    private bool IsAsImported(Guid albumId, string relativePath, long size) =>
        journals?.FindByPath(albumId, relativePath) is { } entry && entry.Size == size;

    /// <summary>What another server journaled about this very file, when that includes its position.</summary>
    private MediaInfo? FromJournals(Guid albumId, string relativePath, long size)
    {
        if (journals?.FindByPath(albumId, relativePath) is not { Latitude: { } latitude, Longitude: { } longitude } entry || entry.Size != size)
        {
            return null;
        }
        DateTime? taken = DateTime.TryParseExact(entry.TakenAt, WallClock, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
        return new MediaInfo(taken, new GeoPoint(latitude, longitude));
    }

    /// <summary>Records a file just placed in an album folder by the import, so it does not need to be read again.</summary>
    public void Record(Album album, string fullPath, DateTime takenAt, GeoPoint? location)
    {
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            return;
        }
        var relative = Relative(album.DestinationRoot, fullPath);
        Write(connection =>
        {
            Upsert(connection, album.Id, relative, file, takenAt, location, null);
            TagCatalog.Store(connection, album.Id, relative, []);
        });
    }

    /// <summary>Photos and videos with a shooting place, newest first, in one album or all of them.</summary>
    public List<LocatedMedia> ListLocated(Guid? albumId = null)
    {
        using var connection = database.Open();
        return connection.Query(
            """
            SELECT m.album_id, a.destination_root, m.path, m.taken_at, m.latitude, m.longitude, m.is_video, m.size
            FROM media m JOIN albums a ON a.id = m.album_id
            WHERE m.latitude IS NOT NULL AND m.longitude IS NOT NULL AND ($album IS NULL OR m.album_id = $album)
            ORDER BY m.taken_at DESC
            """,
            r => new LocatedMedia(
                Guid.Parse(r.GetString(0)),
                Path.Combine(r.GetString(1), r.GetString(2).Replace('/', Path.DirectorySeparatorChar)),
                r.IsDBNull(3) ? DateTime.MinValue : DateTime.ParseExact(r.GetString(3), WallClock, CultureInfo.InvariantCulture),
                r.GetDouble(4), r.GetDouble(5), r.GetInt64(6) != 0, r.GetInt64(7)),
            ("$album", albumId?.ToString()));
    }

    /// <summary>
    /// Indexed photos and videos without a shooting place, in one album or all of them, taken from <paramref name="from"/>
    /// (included) until <paramref name="until"/> (excluded) when given.
    /// </summary>
    public int CountUnlocated(Guid? albumId = null, DateTime? from = null, DateTime? until = null)
    {
        using var connection = database.Open();
        // Wall-clock dates are stored as sortable text: they compare as strings.
        return Convert.ToInt32(connection.Scalar(
            """
            SELECT COUNT(*) FROM media m JOIN albums a ON a.id = m.album_id
            WHERE (m.latitude IS NULL OR m.longitude IS NULL) AND ($album IS NULL OR m.album_id = $album)
              AND ($from IS NULL OR m.taken_at >= $from) AND ($until IS NULL OR m.taken_at < $until)
            """,
            ("$album", albumId?.ToString()),
            ("$from", from?.ToString(WallClock, CultureInfo.InvariantCulture)),
            ("$until", until?.ToString(WallClock, CultureInfo.InvariantCulture))), CultureInfo.InvariantCulture);
    }

    /// <summary>The cached entry of a file, or null when it has not been indexed yet.</summary>
    public MediaInfo? Get(Album album, string fullPath)
    {
        using var connection = database.Open();
        return connection.Query(
            "SELECT taken_at, latitude, longitude FROM media WHERE album_id = $album AND path = $path",
            r => new MediaInfo(
                r.IsDBNull(0) ? null : DateTime.ParseExact(r.GetString(0), WallClock, CultureInfo.InvariantCulture),
                r.IsDBNull(1) || r.IsDBNull(2) ? null : new GeoPoint(r.GetDouble(1), r.GetDouble(2))),
            ("$album", album.Id.ToString()), ("$path", Relative(album.DestinationRoot, fullPath))).Cast<MediaInfo?>().SingleOrDefault();
    }

    /// <summary>
    /// Media files of an album folder, with the same rules as <see cref="PhotoLibrary"/>, and the XMP sidecars found along
    /// (by full path), listed at the same time: a second walk of a network share would take as long again.
    /// </summary>
    private static (List<FileInfo> Media, Dictionary<string, FileInfo> Sidecars) Walk(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var work = Path.Combine(root, DestinationLayout.WorkFolderName);
        var media = new List<FileInfo>();
        var sidecars = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            if (file.FullName.StartsWith(work, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (MediaTypes.IsMedia(file.Name))
            {
                media.Add(file);
            }
            else if (file.Extension.Equals(".xmp", StringComparison.OrdinalIgnoreCase))
            {
                sidecars[file.FullName] = file;
            }
        }
        return (media, sidecars);
    }

    /// <summary>The state of the sidecar of a media file among those listed, as <see cref="UserTags.FindSidecar"/> picks it.</summary>
    private static string? SidecarOf(string mediaPath, Dictionary<string, FileInfo> sidecars) =>
        sidecars.TryGetValue(UserTags.SidecarPath(mediaPath), out var full) ? TagCatalog.SidecarState(full)
        : sidecars.TryGetValue(Path.ChangeExtension(mediaPath, ".xmp"), out var shortName) ? TagCatalog.SidecarState(shortName)
        : null;

    private Dictionary<string, KnownFile> Known(Guid albumId)
    {
        using var connection = database.Open();
        return connection
            .Query("SELECT path, size, modified_at, sidecar, tags_read, taken_at, latitude, longitude FROM media WHERE album_id = $album",
                r => (Path: r.GetString(0), File: new KnownFile(r.GetInt64(1), SqliteExtensions.ParseDate(r.GetString(2)), r.GetNullableString(3),
                    r.GetInt64(4) != 0, new MediaInfo(
                        r.IsDBNull(5) ? null : DateTime.ParseExact(r.GetString(5), WallClock, CultureInfo.InvariantCulture),
                        r.IsDBNull(6) || r.IsDBNull(7) ? null : new GeoPoint(r.GetDouble(6), r.GetDouble(7))))),
                ("$album", albumId.ToString()))
            .ToDictionary(e => e.Path, e => e.File, StringComparer.OrdinalIgnoreCase);
    }

    private List<string> TaggedPaths(Guid albumId)
    {
        using var connection = database.Open();
        return connection.Query("SELECT DISTINCT path FROM media_tags WHERE album_id = $album", r => r.GetString(0), ("$album", albumId.ToString()));
    }

    private sealed record KnownFile(long Size, DateTimeOffset ModifiedAt, string? Sidecar, bool TagsRead, MediaInfo Info);

    private void Write(Action<SqliteConnection> action)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        action(connection);
        transaction.Commit();
    }

    // Tags read from the file itself: no journal date to weigh them against (tags_at).
    private static void Upsert(SqliteConnection connection, Guid albumId, string path, FileInfo file, DateTime takenAt, GeoPoint? location,
        string? sidecar) =>
        connection.Execute(
            """
            INSERT INTO media (album_id, path, size, modified_at, taken_at, latitude, longitude, is_video, sidecar, tags_read, tags_at)
            VALUES ($album, $path, $size, $modified, $taken, $lat, $lon, $video, $sidecar, 1, NULL)
            ON CONFLICT (album_id, path) DO UPDATE SET
                size = excluded.size, modified_at = excluded.modified_at, taken_at = excluded.taken_at,
                latitude = excluded.latitude, longitude = excluded.longitude, is_video = excluded.is_video,
                sidecar = excluded.sidecar, tags_read = 1, tags_at = NULL
            """,
            ("$album", albumId.ToString()), ("$path", path), ("$size", file.Length), ("$modified", Modified(file).ToDb()),
            ("$taken", takenAt.ToString(WallClock, CultureInfo.InvariantCulture)), ("$lat", location?.Latitude),
            ("$lon", location?.Longitude), ("$video", MediaTypes.IsVideo(file.Name) ? 1 : 0), ("$sidecar", sidecar));

    private static DateTimeOffset Modified(FileInfo file) => new(file.LastWriteTimeUtc, TimeSpan.Zero);

    /// <summary>Path relative to the album folder, with '/' separators (the same on Windows and on the NAS).</summary>
    private static string Relative(string root, string fullPath) => Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static DateTime Truncate(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Unspecified);
}
