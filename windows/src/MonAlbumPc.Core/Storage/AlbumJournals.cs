using System.Globalization;
using System.Text;
using System.Text.Json;
using MonAlbumPc.Core.Import;

namespace MonAlbumPc.Core.Storage;

/// <summary>One import, as written in an album journal (one JSON object per line).</summary>
public sealed record JournalEntry(
    string Sha256,
    string Name,
    long Size,
    string Path,
    string? TakenAt,
    string? Device,
    DateTimeOffset ImportedAt);

/// <summary>
/// Import journals kept next to the photos, in <c>{album}\.monalbumpc\journal\{serverId}.jsonl</c>, so that several
/// servers writing to the same folder (a NAS and a PC, for instance) never import the same photo twice. Each server only
/// appends to its own file — no concurrent writes over a network share — and reads the files of the others.
/// </summary>
public sealed class AlbumJournals(AlbumStore albums, DeviceStore devices, Guid serverId, TimeProvider clock)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Snapshot> _others = [];
    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

    public static string Folder(string albumRoot) => Path.Combine(albumRoot, DestinationLayout.WorkFolderName, "journal");

    private string OwnFile(string albumRoot) => Path.Combine(Folder(albumRoot), serverId + ".jsonl");

    /// <summary>Adds an import of this server to the album journal. Best effort: the local catalog stays the reference.</summary>
    public void Append(ImportRecord record)
    {
        if (albums.Get(record.AlbumId) is not { } album || !Directory.Exists(album.DestinationRoot))
        {
            return;
        }
        var device = record.DeviceId is { } id ? devices.Get(id)?.Name : null;
        var line = JsonSerializer.Serialize(ToEntry(album.DestinationRoot, record, device), Json) + "\n";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Folder(album.DestinationRoot));
                File.AppendAllText(OwnFile(album.DestinationRoot), line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Network folder gone for a moment: the next export catches up (see ExportMissing).
            }
        }
    }

    /// <summary>
    /// Writes this server's journal for albums that do not have one yet (imports made before journals existed, or an
    /// album moved to a new folder).
    /// </summary>
    public void ExportMissing(ImportHistory history)
    {
        foreach (var album in albums.List())
        {
            var path = OwnFile(album.DestinationRoot);
            if (!Directory.Exists(album.DestinationRoot) || File.Exists(path))
            {
                continue;
            }
            var names = new Dictionary<Guid, string?>();
            var lines = history.ListByAlbum(album.Id).Select(record =>
            {
                string? device = null;
                if (record.DeviceId is { } id && !names.TryGetValue(id, out device))
                {
                    device = names[id] = devices.Get(id)?.Name;
                }
                return JsonSerializer.Serialize(ToEntry(album.DestinationRoot, record, device), Json);
            });
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Folder(album.DestinationRoot));
                    File.WriteAllLines(path + ".tmp", lines, Encoding.UTF8);
                    File.Move(path + ".tmp", path, overwrite: false);
                }
                catch (IOException)
                {
                    // Retried at the next start.
                }
            }
        }
    }

    public bool ContainsSha(Guid albumId, string sha256) => Others(albumId).Shas.Contains(sha256);

    public bool ContainsNameAndSize(Guid albumId, string name, long size) =>
        Others(albumId).NameSizes.Contains(NameSize(name, size));

    public bool ContainsNameAndDate(Guid albumId, string name, DateTime takenAt) =>
        Others(albumId).NameDates.Contains(NameDate(name, takenAt));

    /// <summary>What the other servers imported into this album, reloaded at most every 30 seconds.</summary>
    private Snapshot Others(Guid albumId)
    {
        lock (_lock)
        {
            if (_others.TryGetValue(albumId, out var cached) && clock.GetUtcNow() - cached.LoadedAt < RefreshInterval)
            {
                return cached;
            }
            var snapshot = new Snapshot(clock.GetUtcNow());
            if (albums.Get(albumId) is { } album && Directory.Exists(Folder(album.DestinationRoot)))
            {
                foreach (var file in Directory.EnumerateFiles(Folder(album.DestinationRoot), "*.jsonl"))
                {
                    if (!Path.GetFileNameWithoutExtension(file).Equals(serverId.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var entry in Read(file))
                        {
                            snapshot.Add(entry);
                        }
                    }
                }
            }
            _others[albumId] = snapshot;
            return snapshot;
        }
    }

    /// <summary>Entries of a journal, reading only what was appended since the last time.</summary>
    private List<JournalEntry> Read(string file)
    {
        if (!_files.TryGetValue(file, out var state))
        {
            state = _files[file] = new FileState();
        }
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < state.Offset)
            {
                state.Offset = 0; // file replaced
                state.Entries.Clear();
            }
            stream.Seek(state.Offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            var complete = text.LastIndexOf('\n') + 1; // a line still being written is read next time
            foreach (var line in text[..complete].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    if (JsonSerializer.Deserialize<JournalEntry>(line, Json) is { } entry)
                    {
                        state.Entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // A damaged line does not hide the others.
                }
            }
            state.Offset += Encoding.UTF8.GetByteCount(text[..complete]);
        }
        catch (IOException)
        {
            // Unreadable for now: keep what was read before.
        }
        return state.Entries;
    }

    private static JournalEntry ToEntry(string root, ImportRecord record, string? device) => new(
        record.Sha256,
        record.OriginalName,
        record.Size,
        Path.GetRelativePath(root, record.DestPath).Replace('\\', '/'),
        record.TakenAt?.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
        device,
        record.ImportedAt);

    private static string NameSize(string name, long size) => $"{name.ToLowerInvariant()}|{size}";

    private static string NameDate(string name, DateTime takenAt) =>
        $"{name.ToLowerInvariant()}|{takenAt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}";

    private sealed class FileState
    {
        public long Offset { get; set; }
        public List<JournalEntry> Entries { get; } = [];
    }

    private sealed class Snapshot(DateTimeOffset loadedAt)
    {
        public DateTimeOffset LoadedAt { get; } = loadedAt;
        public HashSet<string> Shas { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NameSizes { get; } = [];
        public HashSet<string> NameDates { get; } = [];

        public void Add(JournalEntry entry)
        {
            Shas.Add(entry.Sha256);
            NameSizes.Add(NameSize(entry.Name, entry.Size));
            if (entry.TakenAt is not null)
            {
                NameDates.Add($"{entry.Name.ToLowerInvariant()}|{entry.TakenAt}");
            }
        }
    }
}
