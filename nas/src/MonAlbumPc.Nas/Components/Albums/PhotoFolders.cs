using MonAlbumPc.Core;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Nas.Components.Albums;

/// <summary>Album folders on the NAS: always under the photos root (the <c>/photos</c> volume).</summary>
public sealed class PhotoFolders(NasOptions options)
{
    public string Root { get; } = Path.GetFullPath(options.PhotosRoot);

    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Folder proposed for a new album: <c>/photos/{Nom}</c>.</summary>
    public string Suggest(string name)
    {
        var clean = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\' ? '_' : c))
            .Trim('.', ' ');
        return clean.Length == 0 ? "" : Path.Combine(Root, clean);
    }

    public bool IsRoot(string path) => string.Equals(Normalize(path), Normalize(Root), Comparison);

    public bool IsTrash(string path) => IsUnder(path, options.TrashDirectory);

    /// <summary>True when the folder can hold an album: under the root, not the root itself nor the trash.</summary>
    public bool IsValidAlbumFolder(string path) =>
        path.Length > 0 && Path.IsPathRooted(path) && options.IsUnderPhotos(path) && !IsRoot(path) && !IsTrash(path);

    public static bool SameFolder(string a, string b) => string.Equals(Normalize(a), Normalize(b), Comparison);

    /// <summary>Visible subfolders (hidden ones such as the trash are left out), sorted by name.</summary>
    public List<string> Subfolders(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .Order(StringComparer.Create(Shared.Formatting.French, ignoreCase: true))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The deepest existing folder of <paramref name="path"/> inside the root, or the root.</summary>
    public string ExistingAncestor(string? path)
    {
        var current = string.IsNullOrWhiteSpace(path) || !options.IsUnderPhotos(path) ? Root : Path.GetFullPath(path);
        while (!Directory.Exists(current) && !IsRoot(current) && Path.GetDirectoryName(current) is { } parent)
        {
            current = parent;
        }
        return current;
    }

    /// <summary>Creates <paramref name="name"/> inside <paramref name="parent"/>. Returns an error message, or null.</summary>
    public string? CreateSubfolder(string parent, string name, out string created)
    {
        created = "";
        name = name.Trim();
        if (name.Length == 0 || name.StartsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            return "Ce nom de dossier n'est pas valable.";
        }
        var path = Path.Combine(parent, name);
        if (!options.IsUnderPhotos(path))
        {
            return "Le dossier doit se trouver dans " + Root + ".";
        }
        try
        {
            Directory.CreateDirectory(path);
            created = path;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Impossible de créer le dossier : {e.Message}";
        }
    }

    /// <summary>Creates the folder and the album. Returns an error message, or null.</summary>
    public string? CreateAlbum(CoreServices core, string name, string folder, out Album? album)
    {
        album = null;
        if (EnsureFolder(folder) is { } error)
        {
            return error;
        }
        album = new Album(Guid.NewGuid(), name.Trim(), folder, core.Albums.NextColor(), core.Clock.GetLocalNow());
        core.Albums.Add(album);
        return null;
    }

    public string? EnsureFolder(string folder)
    {
        if (!IsValidAlbumFolder(folder))
        {
            return $"Choisissez un dossier à l'intérieur de {Root}.";
        }
        try
        {
            Directory.CreateDirectory(folder);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Impossible de créer le dossier {folder}. {e.Message}";
        }
    }

    private bool IsUnder(string path, string parent)
    {
        var p = Normalize(path) + Path.DirectorySeparatorChar;
        var root = Normalize(parent) + Path.DirectorySeparatorChar;
        return p.StartsWith(root, Comparison);
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
