using System.Globalization;
using Microsoft.Data.Sqlite;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tags;

/// <summary>A tag of the library and the number of photos and videos that carry it (0 for a tag created ahead).</summary>
public sealed record TagCount(string Name, int Count);

/// <summary>A file of an album with its user tags; <see cref="Path"/> is relative to the album folder, with '/'.</summary>
public sealed record TaggedFile(Guid AlbumId, string Path, IReadOnlyList<string> Tags);

/// <summary>
/// The user tags of the media index, for instant filtering (the files stay the reference: see <see cref="UserTags"/>), and
/// the tags created ahead of any photo, which the libraries share through the tag journals.
/// </summary>
public sealed class TagCatalog(Database database)
{
    private static readonly CompareInfo French = CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    /// <summary>Every tag, with its number of files, sorted by name (a parent before its children).</summary>
    public List<TagCount> List()
    {
        using var connection = database.Open();
        var counts = new Dictionary<string, int>(UserTags.Same);
        foreach (var (tag, count) in connection.Query(
                     """
                     SELECT t.tag, COUNT(*) FROM media_tags t JOIN albums a ON a.id = t.album_id
                     GROUP BY t.tag COLLATE NOCASE
                     """,
                     r => (r.GetString(0), r.GetInt32(1))))
        {
            counts[tag] = count;
        }
        foreach (var name in connection.Query("SELECT name FROM tag_names WHERE declared = 1", r => r.GetString(0)))
        {
            counts.TryAdd(name, 0);
        }
        return [.. counts.Select(c => new TagCount(c.Key, c.Value)).Order(TagOrder.Instance)];
    }

    /// <summary>The tags of every tagged file, in one album or all of them.</summary>
    public List<TaggedFile> Files(Guid? albumId = null)
    {
        using var connection = database.Open();
        return connection.Query(
                "SELECT album_id, path, tag FROM media_tags WHERE $album IS NULL OR album_id = $album ORDER BY album_id, path",
                r => (Album: Guid.Parse(r.GetString(0)), Path: r.GetString(1), Tag: r.GetString(2)), ("$album", albumId?.ToString()))
            .GroupBy(r => (r.Album, r.Path))
            .Select(g => new TaggedFile(g.Key.Album, g.Key.Path, [.. g.Select(r => r.Tag)]))
            .ToList();
    }

    /// <summary>The files carrying <paramref name="tag"/> or a tag under it.</summary>
    public List<TaggedFile> FilesWithin(string tag)
    {
        var wanted = Files().Where(f => f.Tags.Any(t => UserTags.IsWithin(t, tag))).ToList();
        return wanted;
    }

    /// <summary>The tags of one file of an album.</summary>
    public List<string> Of(Guid albumId, string relativePath)
    {
        using var connection = database.Open();
        return connection.Query("SELECT tag FROM media_tags WHERE album_id = $album AND path = $path",
            r => r.GetString(0), ("$album", albumId.ToString()), ("$path", relativePath));
    }

    /// <summary>The spelling the library already uses for <paramref name="tag"/> ("Boulot" typed, "boulot" known), else itself.</summary>
    public string Canonical(string tag) => List().FirstOrDefault(t => UserTags.Same.Equals(t.Name, tag))?.Name ?? tag;

    /// <summary>
    /// Records that a tag was created (<paramref name="declared"/>) or deleted at <paramref name="at"/>, unless a later
    /// change is already known. True when the list changed.
    /// </summary>
    public bool Declare(string tag, bool declared, DateTimeOffset at)
    {
        using var connection = database.Open();
        var known = connection.Query("SELECT declared, at FROM tag_names WHERE name = $name",
            r => (Declared: r.GetInt64(0) != 0, At: SqliteExtensions.ParseDate(r.GetString(1))), ("$name", tag)).ToList();
        if (known is [var row] && row.At >= at)
        {
            return false;
        }
        connection.Execute(
            """
            INSERT INTO tag_names (name, declared, at) VALUES ($name, $declared, $at)
            ON CONFLICT (name) DO UPDATE SET name = excluded.name, declared = excluded.declared, at = excluded.at
            """,
            ("$name", tag), ("$declared", declared ? 1 : 0), ("$at", at.ToDb()));
        // A deletion of a tag never declared here is kept (against older creations) but changes nothing visible.
        return known is [var previous] ? previous.Declared != declared : declared;
    }

    /// <summary>
    /// The new tags of a file this server just wrote, with its new size and sidecar: the index needs not read it again. A
    /// file the index has not met yet (album just added) gets its tags all the same, for the filters.
    /// </summary>
    public void Set(Guid albumId, string relativePath, string fullPath, IReadOnlyList<string> tags, DateTimeOffset at)
    {
        var file = new FileInfo(fullPath);
        if (!file.Exists)
        {
            return;
        }
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        Update(connection, albumId, relativePath, file, at);
        Store(connection, albumId, relativePath, tags);
        transaction.Commit();
    }

    /// <summary>
    /// Takes the tags another server journaled for a file, when the file is still the one it wrote (same size, not
    /// modified since) and nothing more recent is known about it. True when the tags of the file changed.
    /// </summary>
    public bool Apply(Guid albumId, string albumRoot, TagJournalEntry entry)
    {
        if (entry.Path is not { } relative || entry.Tags is not { } tags)
        {
            return false;
        }
        var full = Path.Combine(albumRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        var file = new FileInfo(full);
        if (!file.Exists || file.Length != entry.Size || file.LastWriteTimeUtc > entry.At.UtcDateTime)
        {
            return false; // changed since: the index reads the file itself
        }
        using var connection = database.Open();
        var row = connection.Query("SELECT tags_at FROM media WHERE album_id = $album AND path = $path",
            r => r.GetNullableDate(0), ("$album", albumId.ToString()), ("$path", relative)).ToList();
        if (row is [var taggedAt] && taggedAt >= entry.At)
        {
            return false;
        }
        var before = Of(albumId, relative);
        using var transaction = connection.BeginTransaction();
        Update(connection, albumId, relative, file, entry.At);
        Store(connection, albumId, relative, tags);
        transaction.Commit();
        return !before.Order(UserTags.Same).SequenceEqual(tags.Order(UserTags.Same), UserTags.Same);
    }

    // Size, date and sidecar of the file as now written, so that the next refresh does not read it again.
    private static void Update(SqliteConnection connection, Guid albumId, string relativePath, FileInfo file, DateTimeOffset at) =>
        connection.Execute(
            """
            UPDATE media SET size = $size, modified_at = $modified, sidecar = $sidecar, tags_read = 1, tags_at = $at
            WHERE album_id = $album AND path = $path
            """,
            ("$album", albumId.ToString()), ("$path", relativePath), ("$size", file.Length),
            ("$modified", new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero).ToDb()),
            ("$sidecar", SidecarState(file.FullName)), ("$at", at.ToDb()));

    /// <summary>What the index remembers of the sidecar of a file, to notice when another program changes it.</summary>
    public static string? SidecarState(string mediaPath) =>
        UserTags.FindSidecar(mediaPath) is { } sidecar ? SidecarState(new FileInfo(sidecar)) : null;

    internal static string SidecarState(FileInfo sidecar) =>
        $"{Path.GetFileName(sidecar.Name)}:{sidecar.LastWriteTimeUtc.Ticks}:{sidecar.Length}";

    internal static void Store(SqliteConnection connection, Guid albumId, string relativePath, IEnumerable<string> tags)
    {
        connection.Execute("DELETE FROM media_tags WHERE album_id = $album AND path = $path",
            ("$album", albumId.ToString()), ("$path", relativePath));
        foreach (var tag in tags.Distinct(UserTags.Same))
        {
            connection.Execute("INSERT INTO media_tags (album_id, path, tag) VALUES ($album, $path, $tag)",
                ("$album", albumId.ToString()), ("$path", relativePath), ("$tag", tag));
        }
    }

    /// <summary>By name, ignoring case and accents as a French reader expects, each parent right before its children.</summary>
    public sealed class TagOrder : IComparer<TagCount>, IComparer<string>
    {
        public static readonly TagOrder Instance = new();

        public int Compare(TagCount? x, TagCount? y) => Compare(x?.Name, y?.Name);

        public int Compare(string? x, string? y)
        {
            var a = (x ?? "").Split(UserTags.Separator);
            var b = (y ?? "").Split(UserTags.Separator);
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var order = French.Compare(a[i], b[i], CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (order != 0)
                {
                    return order;
                }
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}
