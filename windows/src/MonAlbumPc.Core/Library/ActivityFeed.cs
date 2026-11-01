using System.Text;
using System.Text.Json;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Library;

/// <summary>One finished backup, as written in the activity feed (one JSON object per line).</summary>
public sealed record ActivityEntry(
    string Id,
    DateTimeOffset At,
    Guid AlbumId,
    string AlbumName,
    string? Device,
    int Photos,
    int Videos,
    bool Complete,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool HasError => ErrorCode is not null;

    public static ActivityEntry From(BackupSession session, string albumName, string? device) => new(
        Guid.NewGuid().ToString(),
        session.EndedAt,
        session.AlbumId,
        albumName,
        device,
        session.Photos,
        session.Videos,
        session.Complete,
        session.ErrorCode,
        session.ErrorMessage);
}

/// <summary>
/// What the servers tell the viewers, in <c>{library}/.monalbumpc/activity/{serverId}.jsonl</c>. Same philosophy as
/// <see cref="AlbumJournals"/>: append-only, one writer per file, so nothing is ever written concurrently over the network.
/// </summary>
public static class ActivityFeed
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(false);

    public static string Folder(string libraryRoot) => Path.Combine(LibraryManifest.SharedFolder(libraryRoot), "activity");

    public static string FilePath(string libraryRoot, Guid serverId) => Path.Combine(Folder(libraryRoot), serverId + ".jsonl");

    public static void Append(string libraryRoot, Guid serverId, ActivityEntry entry)
    {
        Directory.CreateDirectory(Folder(libraryRoot));
        File.AppendAllBytes(FilePath(libraryRoot, serverId), Utf8.GetBytes(JsonSerializer.Serialize(entry, Json) + "\n"));
    }

    /// <summary>
    /// The lines added since <paramref name="positions"/> (byte offset per file name), which it moves forward. A line still
    /// being written is read next time; a damaged one is skipped. An unreachable feed returns nothing and moves nothing.
    /// </summary>
    public static List<ActivityEntry> ReadNew(string libraryRoot, IDictionary<string, long> positions)
    {
        var entries = new List<ActivityEntry>();
        foreach (var file in Files(libraryRoot))
        {
            try
            {
                var name = Path.GetFileName(file);
                var position = positions.TryGetValue(name, out var known) ? known : 0;
                var text = ReadCompleteLines(file, ref position);
                positions[name] = position;
                entries.AddRange(Parse(text));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Unreachable for now: the same lines are read again next time.
            }
        }
        return [.. entries.OrderBy(e => e.At)];
    }

    /// <summary>Moves <paramref name="positions"/> to the end of the feed, without reading the past.</summary>
    public static void SkipToEnd(string libraryRoot, IDictionary<string, long> positions)
    {
        foreach (var file in Files(libraryRoot))
        {
            try
            {
                var position = 0L;
                ReadCompleteLines(file, ref position);
                positions[Path.GetFileName(file)] = position;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Read from its beginning later on: at worst, one summary too many.
            }
        }
    }

    private static List<string> Files(string libraryRoot)
    {
        try
        {
            return Directory.Exists(Folder(libraryRoot)) ? [.. Directory.EnumerateFiles(Folder(libraryRoot), "*.jsonl")] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Text from <paramref name="position"/> to the last line break, moving the position just after it.</summary>
    private static string ReadCompleteLines(string file, ref long position)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < position)
        {
            position = 0; // file replaced
        }
        var bytes = new byte[stream.Length - position];
        stream.Seek(position, SeekOrigin.Begin);
        var read = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        var length = Array.LastIndexOf(bytes, (byte)'\n', Math.Max(read - 1, 0), read) + 1;
        position += length;
        return Utf8.GetString(bytes, 0, length);
    }

    private static IEnumerable<ActivityEntry> Parse(string text)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            ActivityEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<ActivityEntry>(line.Trim('\r', '﻿'), Json);
            }
            catch (JsonException)
            {
                // A damaged line does not hide the others.
            }
            if (entry is not null)
            {
                yield return entry;
            }
        }
    }
}
