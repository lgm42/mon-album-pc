using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Storage;

public sealed class AlbumStore(Database database)
{
    private const string Columns = "id, name, destination_root, color, created_at";

    public void Add(Album album)
    {
        using var connection = database.Open();
        connection.Execute(
            $"INSERT INTO albums ({Columns}) VALUES ($id, $name, $root, $color, $created)",
            ("$id", album.Id.ToString()), ("$name", album.Name), ("$root", album.DestinationRoot),
            ("$color", album.Color), ("$created", album.CreatedAt.ToDb()));
    }

    public void Update(Album album)
    {
        using var connection = database.Open();
        connection.Execute(
            "UPDATE albums SET name = $name, destination_root = $root, color = $color WHERE id = $id",
            ("$id", album.Id.ToString()), ("$name", album.Name), ("$root", album.DestinationRoot), ("$color", album.Color));
    }

    /// <summary>Deletes an album. Refused while active devices are still paired with it.</summary>
    public void Delete(Guid id)
    {
        using var connection = database.Open();
        var devices = Convert.ToInt64(connection.Scalar(
            "SELECT COUNT(*) FROM devices WHERE album_id = $id AND revoked_at IS NULL", ("$id", id.ToString())));
        if (devices > 0)
        {
            throw new InvalidOperationException("Impossible de supprimer un album qui a encore des téléphones.");
        }
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM devices WHERE album_id = $id", ("$id", id.ToString()));
        connection.Execute("DELETE FROM index_entries WHERE album_id = $id", ("$id", id.ToString()));
        connection.Execute("DELETE FROM media WHERE album_id = $id", ("$id", id.ToString()));
        connection.Execute("DELETE FROM albums WHERE id = $id", ("$id", id.ToString()));
        transaction.Commit();
    }

    public Album? Get(Guid id)
    {
        using var connection = database.Open();
        return connection.Query($"SELECT {Columns} FROM albums WHERE id = $id", Map, ("$id", id.ToString())).SingleOrDefault();
    }

    public List<Album> List()
    {
        using var connection = database.Open();
        return connection.Query($"SELECT {Columns} FROM albums ORDER BY created_at", Map);
    }

    /// <summary>Next palette color, cycling once every color is used.</summary>
    public string NextColor()
    {
        var count = List().Count;
        return Album.Palette[count % Album.Palette.Count];
    }

    private static Album Map(Microsoft.Data.Sqlite.SqliteDataReader r) =>
        new(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), SqliteExtensions.ParseDate(r.GetString(4)));
}
