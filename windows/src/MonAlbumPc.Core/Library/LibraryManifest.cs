using System.Text;
using System.Text.Json;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Library;

/// <summary>An album of the shared library; <see cref="Folder"/> is relative to the library root, with '/' separators.</summary>
public sealed record LibraryAlbum(Guid Id, string Name, string Color, string Folder);

/// <summary>
/// What the server publishes for the viewers in <c>{library}/.monalbumpc/library.json</c>. Each machine only knows its own
/// path to the library, so everything here is relative. The server is the only writer.
/// </summary>
public sealed record LibraryManifest(int Format, string ServerName, IReadOnlyList<LibraryAlbum> Albums)
{
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string SharedFolder(string libraryRoot) => Path.Combine(libraryRoot, DestinationLayout.WorkFolderName);

    public static string FilePath(string libraryRoot) => Path.Combine(SharedFolder(libraryRoot), "library.json");

    /// <summary>The published manifest, or null when it is missing, damaged, or written by a newer version.</summary>
    public static LibraryManifest? Read(string libraryRoot)
    {
        try
        {
            return JsonSerializer.Deserialize<LibraryManifest>(File.ReadAllText(FilePath(libraryRoot)), Json) is
                { Format: >= 1 and <= CurrentFormat, Albums: not null } manifest ? manifest : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Replaces the manifest in one step: a viewer never reads a half-written file.</summary>
    public static void Write(string libraryRoot, LibraryManifest manifest)
    {
        Directory.CreateDirectory(SharedFolder(libraryRoot));
        var target = FilePath(libraryRoot);
        var temporary = target + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, Json), new UTF8Encoding(false));
        File.Move(temporary, target, overwrite: true);
    }

    /// <summary>The manifest of <paramref name="albums"/>. Albums outside the library cannot be reached by the viewers: they are left out.</summary>
    public static LibraryManifest From(string libraryRoot, string serverName, IEnumerable<Album> albums) => new(
        CurrentFormat,
        serverName,
        [.. albums
            .Select(album => (Album: album, Folder: RelativeFolder(libraryRoot, album.DestinationRoot)))
            .Where(a => a.Folder is not null)
            .Select(a => new LibraryAlbum(a.Album.Id, a.Album.Name, a.Album.Color, a.Folder!))]);

    /// <summary>Path of an album folder relative to the library, or null when it is not inside it.</summary>
    public static string? RelativeFolder(string libraryRoot, string albumRoot)
    {
        var relative = Path.GetRelativePath(libraryRoot, albumRoot).Replace('\\', '/');
        return relative == "." || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : relative;
    }

    /// <summary>
    /// Local path of a published album folder. The manifest comes from a network folder: a folder that would lead outside
    /// the library, or into its working folder, gives null.
    /// </summary>
    public static string? AlbumRoot(string libraryRoot, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || folder[0] is '/' or '\\' || Path.IsPathRooted(folder))
        {
            return null;
        }
        var parts = folder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] == DestinationLayout.WorkFolderName || parts.Any(part => part is "." or ".."))
        {
            return null;
        }
        return Path.Combine([libraryRoot, .. parts]);
    }
}
