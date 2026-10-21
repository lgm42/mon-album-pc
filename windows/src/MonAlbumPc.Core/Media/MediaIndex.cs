using System.Globalization;
using Microsoft.Data.Sqlite;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Media;

/// <summary>A photo or video of the catalog with its shooting place.</summary>
public sealed record LocatedMedia(Guid AlbumId, string Path, DateTime TakenAt, double Latitude, double Longitude, bool IsVideo, long Size);

/// <summary>Where a refresh stands: <paramref name="Done"/> files read out of <paramref name="Total"/> new or changed ones.</summary>
public readonly record struct MediaIndexProgress(string AlbumName, int Done, int Total);

/// <summary>What a refresh changed in the catalog.</summary>
public readonly record struct MediaIndexResult(int Read, int Removed)
{
    public bool Changed => Read > 0 || Removed > 0;
}

/// <summary>
/// Metadata of every media file found in the album folders (shooting date, GPS position), cached in the <c>media</c>
/// table so that the map and the viewer do not open each file again. Files are read again only when their size or
/// modification date changes.
/// </summary>
public sealed class MediaIndex(Database database)
{
    private const string WallClock = "yyyy-MM-dd'T'HH:mm:ss";
    private const int BatchSize = 100;

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
        foreach (var file in Walk(album.DestinationRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Relative(album.DestinationRoot, file.FullName);
            seen.Add(relative);
            if (!known.TryGetValue(relative, out var state) || state.Size != file.Length || state.ModifiedAt != Modified(file))
            {
                toRead.Add(file);
            }
        }

        var removed = known.Keys.Where(path => !seen.Contains(path)).ToList();
        foreach (var chunk in removed.Chunk(BatchSize))
        {
            Write(connection =>
            {
                foreach (var path in chunk)
                {
                    connection.Execute("DELETE FROM media WHERE album_id = $album AND path = $path",
                        ("$album", album.Id.ToString()), ("$path", path));
                }
            });
        }

        var done = 0;
        progress?.Report(new MediaIndexProgress(album.Name, 0, toRead.Count));
        foreach (var chunk in toRead.Chunk(BatchSize / 4))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<(FileInfo File, MediaInfo Info)>();
            foreach (var file in chunk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    file.Refresh();
                    if (file.Exists)
                    {
                        entries.Add((file, MediaMetadata.Read(file.FullName)));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Read again at the next refresh.
                }
            }
            Write(connection =>
            {
                foreach (var (file, info) in entries)
                {
                    var taken = info.TakenAt ?? TakenDateResolver.ParseFileName(file.Name) ?? Truncate(file.LastWriteTime);
                    Upsert(connection, album.Id, Relative(album.DestinationRoot, file.FullName), file, taken, info.Location);
                }
            });
            done += chunk.Length;
            progress?.Report(new MediaIndexProgress(album.Name, done, toRead.Count));
        }
        return new MediaIndexResult(toRead.Count, removed.Count);
    }

    /// <summary>Records a file just placed in an album folder by the import, so it does not need to be read again.</summary>
    public void Record(Album album, string fullPath, DateTime takenAt, GeoPoint? location)
    {
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            return;
        }
        Write(connection => Upsert(connection, album.Id, Relative(album.DestinationRoot, fullPath), file, takenAt, location));
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

    /// <summary>Media files of an album folder, with the same rules as <see cref="PhotoLibrary"/>.</summary>
    private static IEnumerable<FileInfo> Walk(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var work = Path.Combine(root, DestinationLayout.WorkFolderName);
        return new DirectoryInfo(root)
            .EnumerateFiles("*", options)
            .Where(f => MediaTypes.IsMedia(f.Name) && !f.FullName.StartsWith(work, StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, (long Size, DateTimeOffset ModifiedAt)> Known(Guid albumId)
    {
        using var connection = database.Open();
        return connection
            .Query("SELECT path, size, modified_at FROM media WHERE album_id = $album",
                r => (Path: r.GetString(0), Size: r.GetInt64(1), Modified: SqliteExtensions.ParseDate(r.GetString(2))),
                ("$album", albumId.ToString()))
            .ToDictionary(e => e.Path, e => (e.Size, e.Modified), StringComparer.OrdinalIgnoreCase);
    }

    private void Write(Action<SqliteConnection> action)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        action(connection);
        transaction.Commit();
    }

    private static void Upsert(SqliteConnection connection, Guid albumId, string path, FileInfo file, DateTime takenAt, GeoPoint? location) =>
        connection.Execute(
            """
            INSERT INTO media (album_id, path, size, modified_at, taken_at, latitude, longitude, is_video)
            VALUES ($album, $path, $size, $modified, $taken, $lat, $lon, $video)
            ON CONFLICT (album_id, path) DO UPDATE SET
                size = excluded.size, modified_at = excluded.modified_at, taken_at = excluded.taken_at,
                latitude = excluded.latitude, longitude = excluded.longitude, is_video = excluded.is_video
            """,
            ("$album", albumId.ToString()), ("$path", path), ("$size", file.Length), ("$modified", Modified(file).ToDb()),
            ("$taken", takenAt.ToString(WallClock, CultureInfo.InvariantCulture)), ("$lat", location?.Latitude),
            ("$lon", location?.Longitude), ("$video", MediaTypes.IsVideo(file.Name) ? 1 : 0));

    private static DateTimeOffset Modified(FileInfo file) => new(file.LastWriteTimeUtc, TimeSpan.Zero);

    /// <summary>Path relative to the album folder, with '/' separators (the same on Windows and on the NAS).</summary>
    private static string Relative(string root, string fullPath) => Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    private static DateTime Truncate(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Unspecified);
}
