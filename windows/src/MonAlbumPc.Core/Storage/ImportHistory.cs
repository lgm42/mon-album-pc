using System.Globalization;

namespace MonAlbumPc.Core.Storage;

public enum ImportChannel { Wifi, Usb }

/// <summary>A file imported into an album. <see cref="Sha256"/> is the hash of the original bytes, before any conversion.</summary>
public sealed record ImportRecord(
    string Sha256,
    Guid AlbumId,
    Guid? DeviceId,
    long Size,
    string OriginalName,
    string? SourcePath,
    string DestPath,
    DateTime? TakenAt,
    ImportChannel Channel,
    DateTimeOffset ImportedAt);

/// <summary>A media file found in an album folder by the indexer (imported manually, before the app).</summary>
public sealed record IndexEntry(Guid AlbumId, string Path, string Sha256, long Size, string Name, DateTime? TakenAt, DateTimeOffset ModifiedAt);

/// <summary>What has already been imported or found in each album: the source of truth for duplicates.</summary>
public sealed class ImportHistory(Database database)
{
    private const string WallClock = "yyyy-MM-dd'T'HH:mm:ss";

    public void Record(ImportRecord record)
    {
        using var connection = database.Open();
        connection.Execute(
            """
            INSERT INTO imports (sha256, album_id, device_id, size, original_name, source_path, dest_path, taken_at, channel, imported_at)
            VALUES ($sha, $album, $device, $size, $name, $source, $dest, $taken, $channel, $imported)
            """,
            ("$sha", record.Sha256), ("$album", record.AlbumId.ToString()), ("$device", record.DeviceId?.ToString()),
            ("$size", record.Size), ("$name", record.OriginalName), ("$source", record.SourcePath), ("$dest", record.DestPath),
            ("$taken", ToWallClock(record.TakenAt)), ("$channel", record.Channel.ToString().ToLowerInvariant()),
            ("$imported", record.ImportedAt.ToDb()));
    }

    /// <summary>True when these bytes are already in the album, imported by the app or found by the indexer.</summary>
    public bool Contains(Guid albumId, string sha256)
    {
        using var connection = database.Open();
        return connection.Scalar(
            """
            SELECT 1 WHERE EXISTS (SELECT 1 FROM imports WHERE album_id = $album AND sha256 = $sha)
                        OR EXISTS (SELECT 1 FROM index_entries WHERE album_id = $album AND sha256 = $sha)
            """,
            ("$album", albumId.ToString()), ("$sha", sha256)) is not null;
    }

    /// <summary>Hashes of <paramref name="sha256s"/> already in the album.</summary>
    public HashSet<string> ContainsMany(Guid albumId, IEnumerable<string> sha256s) =>
        sha256s.Where(sha => Contains(albumId, sha)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Quick match without hashing, for a phone that lost its local index (app reinstalled): a file of this album
    /// with the same original name and size was already imported or found in the album folder.
    /// </summary>
    public bool ContainsNameAndSize(Guid albumId, string name, long size)
    {
        using var connection = database.Open();
        return connection.Scalar(
            """
            SELECT 1 WHERE EXISTS (SELECT 1 FROM imports WHERE album_id = $album AND original_name = $name COLLATE NOCASE AND size = $size)
                        OR EXISTS (SELECT 1 FROM index_entries WHERE album_id = $album AND name = $name COLLATE NOCASE AND size = $size)
            """,
            ("$album", albumId.ToString()), ("$name", name), ("$size", size)) is not null;
    }

    /// <summary>
    /// Loose match for files copied by hand before the app existed: same file name and same shooting time
    /// (to the second) already found in the album folder.
    /// </summary>
    public bool ContainsByNameAndDate(Guid albumId, string name, DateTime takenAt)
    {
        using var connection = database.Open();
        return connection.Scalar(
            "SELECT 1 FROM index_entries WHERE album_id = $album AND name = $name COLLATE NOCASE AND taken_at = $taken LIMIT 1",
            ("$album", albumId.ToString()), ("$name", name), ("$taken", ToWallClock(takenAt))) is not null;
    }

    /// <summary>USB fallback when the phone manifest is stale: same phone, same path, same size.</summary>
    public bool ContainsSource(Guid deviceId, string sourcePath, long size)
    {
        using var connection = database.Open();
        return connection.Scalar(
            "SELECT 1 FROM imports WHERE device_id = $device AND source_path = $source AND size = $size LIMIT 1",
            ("$device", deviceId.ToString()), ("$source", sourcePath), ("$size", size)) is not null;
    }

    public long CountByDevice(Guid deviceId)
    {
        using var connection = database.Open();
        return Convert.ToInt64(connection.Scalar("SELECT COUNT(*) FROM imports WHERE device_id = $device", ("$device", deviceId.ToString())));
    }

    /// <summary>When this phone last sent a file, Wi-Fi or USB.</summary>
    public DateTimeOffset? LastImportByDevice(Guid deviceId)
    {
        using var connection = database.Open();
        return connection.Scalar("SELECT MAX(imported_at) FROM imports WHERE device_id = $device", ("$device", deviceId.ToString()))
            is string value ? SqliteExtensions.ParseDate(value) : null;
    }

    /// <summary>The phone a file came from, when the app imported it.</summary>
    public Guid? DeviceOf(string destinationPath)
    {
        using var connection = database.Open();
        return connection.Scalar(
            "SELECT device_id FROM imports WHERE dest_path = $path COLLATE NOCASE ORDER BY id DESC LIMIT 1",
            ("$path", destinationPath)) is string id ? Guid.Parse(id) : null;
    }

    public long CountByAlbum(Guid albumId)
    {
        using var connection = database.Open();
        return Convert.ToInt64(connection.Scalar("SELECT COUNT(*) FROM imports WHERE album_id = $album", ("$album", albumId.ToString())));
    }

    public void UpsertIndexEntry(IndexEntry entry)
    {
        using var connection = database.Open();
        connection.Execute(
            """
            INSERT INTO index_entries (album_id, path, sha256, size, name, taken_at, modified_at)
            VALUES ($album, $path, $sha, $size, $name, $taken, $modified)
            ON CONFLICT (album_id, path) DO UPDATE SET
                sha256 = excluded.sha256, size = excluded.size, name = excluded.name,
                taken_at = excluded.taken_at, modified_at = excluded.modified_at
            """,
            ("$album", entry.AlbumId.ToString()), ("$path", entry.Path), ("$sha", entry.Sha256), ("$size", entry.Size),
            ("$name", entry.Name), ("$taken", ToWallClock(entry.TakenAt)), ("$modified", entry.ModifiedAt.ToDb()));
    }

    /// <summary>Indexed files of an album with the size and date they had when hashed, to skip unchanged files.</summary>
    public Dictionary<string, (long Size, DateTimeOffset ModifiedAt)> GetIndexedFiles(Guid albumId)
    {
        using var connection = database.Open();
        return connection
            .Query("SELECT path, size, modified_at FROM index_entries WHERE album_id = $album",
                r => (Path: r.GetString(0), Size: r.GetInt64(1), Modified: SqliteExtensions.ParseDate(r.GetString(2))),
                ("$album", albumId.ToString()))
            .ToDictionary(e => e.Path, e => (e.Size, e.Modified), StringComparer.OrdinalIgnoreCase);
    }

    public void RemoveIndexEntry(Guid albumId, string path)
    {
        using var connection = database.Open();
        connection.Execute("DELETE FROM index_entries WHERE album_id = $album AND path = $path",
            ("$album", albumId.ToString()), ("$path", path));
    }

    private static string? ToWallClock(DateTime? value) => value?.ToString(WallClock, CultureInfo.InvariantCulture);
}
