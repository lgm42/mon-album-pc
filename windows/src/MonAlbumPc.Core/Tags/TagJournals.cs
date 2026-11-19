using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tags;

/// <summary>
/// One line of a tag journal: either the tags a file got (<see cref="Path"/>, relative to the album with '/',
/// <see cref="Tags"/> after the change and the <see cref="Size"/> of the file once written), or a tag created
/// (<see cref="Declared"/> true) or deleted from the tag list (<see cref="Tag"/>).
/// </summary>
public sealed record TagJournalEntry(
    DateTimeOffset At,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Path = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Tags = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Size = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Declared = null);

/// <summary>
/// Tag journals kept next to the photos, in <c>{album}\.monalbumpc\tags\{serverId}.jsonl</c>: whoever tags a file of an
/// album (the NAS, a Windows viewer over the network share) appends what the file now carries, and the others take it
/// into their index without opening the file again. Needed because tagging keeps the date of the file, and may even keep
/// its size: the index would not notice. As for the import journals, each server only appends to its own file.
/// Created and deleted tags are written to the journal of every album, so that a tag without photos is shared too.
/// </summary>
public sealed class TagJournals(AlbumStore albums, TagCatalog catalog, Guid serverId)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);

    public static string Folder(string albumRoot) => System.IO.Path.Combine(albumRoot, DestinationLayout.WorkFolderName, "tags");

    /// <summary>Adds a line to this server's journal of the album. Best effort: the files and the index stay the reference.</summary>
    public void Append(Album album, TagJournalEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, Json) + "\n";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Folder(album.DestinationRoot));
                File.AppendAllText(System.IO.Path.Combine(Folder(album.DestinationRoot), serverId + ".jsonl"), line, Utf8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Album folder out of reach for a moment: the other servers will read the file itself once it changes.
            }
        }
    }

    /// <summary>Creates or deletes a tag of the list, here and in the journal of every album.</summary>
    public bool Declare(string tag, bool declared, DateTimeOffset at)
    {
        var changed = catalog.Declare(tag, declared, at);
        foreach (var album in albums.List().Where(a => Directory.Exists(a.DestinationRoot)))
        {
            Append(album, new TagJournalEntry(at, Tag: tag, Declared: declared));
        }
        return changed;
    }

    /// <summary>Takes what the other servers journaled since the last look, for every album. Returns the number of changes.</summary>
    public int SyncAll() => albums.List().Sum(Sync);

    /// <summary>Takes what the other servers journaled about <paramref name="album"/> since the last look.</summary>
    public int Sync(Album album)
    {
        var folder = Folder(album.DestinationRoot);
        List<TagJournalEntry> entries = [];
        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    return 0;
                }
                foreach (var file in Directory.EnumerateFiles(folder, "*.jsonl"))
                {
                    if (!System.IO.Path.GetFileNameWithoutExtension(file).Equals(serverId.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        entries.AddRange(ReadNew(file));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return 0; // looked at again next time
            }
        }
        var changes = 0;
        foreach (var entry in entries.OrderBy(e => e.At))
        {
            try
            {
                if (entry.Tag is { } tag && entry.Declared is { } declared ? catalog.Declare(tag, declared, entry.At) : catalog.Apply(album.Id, album.DestinationRoot, entry))
                {
                    changes++;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // That file is out of reach: the index reads it once it can.
            }
        }
        return changes;
    }

    /// <summary>The lines appended to a journal since the last read; a line still being written waits for the next one.</summary>
    private List<TagJournalEntry> ReadNew(string file)
    {
        var offset = _offsets.GetValueOrDefault(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < offset)
        {
            offset = 0; // file replaced
        }
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = reader.ReadToEnd();
        var complete = text.LastIndexOf('\n') + 1;
        List<TagJournalEntry> entries = [];
        foreach (var line in text[..complete].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (JsonSerializer.Deserialize<TagJournalEntry>(line, Json) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A damaged line does not hide the others.
            }
        }
        _offsets[file] = offset + Encoding.UTF8.GetByteCount(text[..complete]);
        return entries;
    }
}
