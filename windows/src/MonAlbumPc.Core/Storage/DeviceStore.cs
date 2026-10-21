using Microsoft.Data.Sqlite;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Storage;

public sealed class DeviceStore(Database database)
{
    private const string Columns = "id, album_id, name, model, secret_hash, app_version, paired_at, last_seen_at, revoked_at, hardware_id";

    public void Add(Device device)
    {
        using var connection = database.Open();
        connection.Execute(
            $"INSERT INTO devices ({Columns}) VALUES ($id, $album, $name, $model, $secret, $version, $paired, $seen, $revoked, $hardware)",
            ("$id", device.Id.ToString()), ("$album", device.AlbumId.ToString()), ("$name", device.Name),
            ("$model", device.Model), ("$secret", device.SecretHash), ("$version", device.AppVersion),
            ("$paired", device.PairedAt.ToDb()), ("$seen", device.LastSeenAt.ToDb()), ("$revoked", device.RevokedAt.ToDb()),
            ("$hardware", device.HardwareId));
    }

    public Device? Get(Guid id)
    {
        using var connection = database.Open();
        return connection.Query($"SELECT {Columns} FROM devices WHERE id = $id", Map, ("$id", id.ToString())).SingleOrDefault();
    }

    /// <summary>Active (non revoked) devices of an album.</summary>
    public List<Device> ListByAlbum(Guid albumId)
    {
        using var connection = database.Open();
        return connection.Query(
            $"SELECT {Columns} FROM devices WHERE album_id = $album AND revoked_at IS NULL ORDER BY paired_at",
            Map, ("$album", albumId.ToString()));
    }

    /// <summary>The phone with this Android ID in the album, revoked or not, most recently paired first.</summary>
    public Device? FindByHardwareId(Guid albumId, string hardwareId)
    {
        using var connection = database.Open();
        return connection.Query(
            $"SELECT {Columns} FROM devices WHERE album_id = $album AND hardware_id = $hardware ORDER BY paired_at DESC LIMIT 1",
            Map, ("$album", albumId.ToString()), ("$hardware", hardwareId)).SingleOrDefault();
    }

    /// <summary>The phone of that name in the album, revoked or not, most recently paired first.</summary>
    public Device? FindByName(Guid albumId, string name)
    {
        using var connection = database.Open();
        return connection.Query(
            $"SELECT {Columns} FROM devices WHERE album_id = $album AND name = $name COLLATE NOCASE ORDER BY paired_at DESC LIMIT 1",
            Map, ("$album", albumId.ToString()), ("$name", name.Trim())).SingleOrDefault();
    }

    /// <summary>
    /// Phones of an album bearing the same name are one phone paired several times: they are folded into the most
    /// recently paired active one (or the most recent one), which keeps working as it is.
    /// </summary>
    public void MergeSameNames()
    {
        using var connection = database.Open();
        var all = connection.Query($"SELECT {Columns} FROM devices", Map);
        foreach (var group in all.GroupBy(d => (d.AlbumId, Name: d.Name.Trim().ToLowerInvariant())).Where(g => g.Count() > 1))
        {
            var kept = group.OrderByDescending(d => d.IsActive).ThenByDescending(d => d.PairedAt).First();
            foreach (var duplicate in group.Where(d => d.Id != kept.Id))
            {
                Merge(duplicate.Id, kept.Id);
            }
        }
    }

    /// <summary>A known phone paired again: new secret and app details, active again, same id and history.</summary>
    public void Repair(Device device)
    {
        using var connection = database.Open();
        connection.Execute(
            """
            UPDATE devices SET model = $model, secret_hash = $secret, app_version = $version, paired_at = $paired,
                               last_seen_at = $seen, revoked_at = NULL, hardware_id = $hardware
            WHERE id = $id
            """,
            ("$id", device.Id.ToString()), ("$model", device.Model), ("$secret", device.SecretHash),
            ("$version", device.AppVersion), ("$paired", device.PairedAt.ToDb()), ("$seen", device.LastSeenAt.ToDb()),
            ("$hardware", device.HardwareId));
    }

    /// <summary>
    /// Merges a duplicate into the phone kept: its imports and backup history move over, then it is removed.
    /// The kept phone stays paired as it is.
    /// </summary>
    public void Merge(Guid duplicateId, Guid keptId)
    {
        if (duplicateId == keptId)
        {
            return;
        }
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("UPDATE imports SET device_id = $kept WHERE device_id = $duplicate",
            ("$kept", keptId.ToString()), ("$duplicate", duplicateId.ToString()));
        connection.Execute("UPDATE sessions SET device_id = $kept WHERE device_id = $duplicate",
            ("$kept", keptId.ToString()), ("$duplicate", duplicateId.ToString()));
        connection.Execute(
            "UPDATE devices SET hardware_id = COALESCE(hardware_id, (SELECT hardware_id FROM devices WHERE id = $duplicate)) WHERE id = $kept",
            ("$kept", keptId.ToString()), ("$duplicate", duplicateId.ToString()));
        connection.Execute("DELETE FROM devices WHERE id = $duplicate", ("$duplicate", duplicateId.ToString()));
        transaction.Commit();
    }

    public void Rename(Guid id, string name)
    {
        using var connection = database.Open();
        connection.Execute("UPDATE devices SET name = $name WHERE id = $id", ("$id", id.ToString()), ("$name", name));
    }

    public void Touch(Guid id, DateTimeOffset seenAt, string? appVersion)
    {
        using var connection = database.Open();
        connection.Execute(
            "UPDATE devices SET last_seen_at = $seen, app_version = COALESCE($version, app_version) WHERE id = $id",
            ("$id", id.ToString()), ("$seen", seenAt.ToDb()), ("$version", appVersion));
    }

    /// <summary>Unpairs a device. Its import history is kept so nothing is imported twice if it comes back.</summary>
    public void Revoke(Guid id, DateTimeOffset revokedAt)
    {
        using var connection = database.Open();
        connection.Execute(
            "UPDATE devices SET revoked_at = $revoked WHERE id = $id AND revoked_at IS NULL",
            ("$id", id.ToString()), ("$revoked", revokedAt.ToDb()));
    }

    private static Device Map(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)),
        Guid.Parse(r.GetString(1)),
        r.GetString(2),
        r.GetString(3),
        (byte[])r[4],
        r.GetNullableString(5),
        SqliteExtensions.ParseDate(r.GetString(6)),
        r.GetNullableDate(7),
        r.GetNullableDate(8),
        r.GetNullableString(9));
}
