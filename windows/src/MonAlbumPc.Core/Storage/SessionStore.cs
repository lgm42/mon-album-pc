using Microsoft.Data.Sqlite;

namespace MonAlbumPc.Core.Storage;

/// <summary>One backup of a phone: what arrived over a continuous stretch of time, and how it ended.</summary>
public sealed record BackupSession(
    long Id,
    Guid DeviceId,
    Guid AlbumId,
    ImportChannel Channel,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int Photos,
    int Videos,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool HasError => ErrorCode is not null;
}

public sealed class SessionStore(Database database)
{
    private const string Columns = "id, device_id, album_id, channel, started_at, ended_at, photos, videos, error_code, error_message";

    public long Add(BackupSession session)
    {
        using var connection = database.Open();
        connection.Execute(
            """
            INSERT INTO sessions (device_id, album_id, channel, started_at, ended_at, photos, videos, error_code, error_message)
            VALUES ($device, $album, $channel, $started, $ended, $photos, $videos, $code, $message)
            """,
            ("$device", session.DeviceId.ToString()), ("$album", session.AlbumId.ToString()),
            ("$channel", session.Channel.ToString().ToLowerInvariant()), ("$started", session.StartedAt.ToDb()),
            ("$ended", session.EndedAt.ToDb()), ("$photos", session.Photos), ("$videos", session.Videos),
            ("$code", session.ErrorCode), ("$message", session.ErrorMessage));
        return Convert.ToInt64(connection.Scalar("SELECT last_insert_rowid()"));
    }

    /// <summary>Most recent sessions first.</summary>
    public List<BackupSession> List(int limit = 500)
    {
        using var connection = database.Open();
        return connection.Query($"SELECT {Columns} FROM sessions ORDER BY started_at DESC LIMIT $limit", Map, ("$limit", limit));
    }

    private static BackupSession Map(SqliteDataReader r) => new(
        r.GetInt64(0),
        Guid.Parse(r.GetString(1)),
        Guid.Parse(r.GetString(2)),
        Enum.Parse<ImportChannel>(r.GetString(3), ignoreCase: true),
        SqliteExtensions.ParseDate(r.GetString(4)),
        SqliteExtensions.ParseDate(r.GetString(5)),
        r.GetInt32(6),
        r.GetInt32(7),
        r.GetNullableString(8),
        r.GetNullableString(9));
}
