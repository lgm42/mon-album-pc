namespace MonAlbumPc.Nas.Components.Photos;

/// <summary>Deleting from the web moves the file to the NAS trash folder, keeping its path relative to the photos root.</summary>
public static class PhotoTrash
{
    /// <summary>Moves the file. Returns an error message, or null.</summary>
    public static string? Move(NasOptions options, string path)
    {
        if (!options.IsUnderPhotos(path) || IsInTrash(options, path))
        {
            return "Seules les photos du dossier des photos du NAS peuvent être supprimées ici.";
        }
        var relative = Path.GetRelativePath(options.PhotosRoot, path);
        var target = Path.Combine(options.TrashDirectory, relative);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(path, Unique(target));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Impossible de déplacer le fichier dans la corbeille : {e.Message}";
        }
    }

    private static bool IsInTrash(NasOptions options, string path)
    {
        var trash = Path.GetFullPath(options.TrashDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(trash, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    // "IMG_1.jpg" already in the trash: "IMG_1 (2).jpg".
    private static string Unique(string target)
    {
        var candidate = target;
        for (var n = 2; File.Exists(candidate); n++)
        {
            candidate = Path.Combine(Path.GetDirectoryName(target)!, $"{Path.GetFileNameWithoutExtension(target)} ({n}){Path.GetExtension(target)}");
        }
        return candidate;
    }
}
