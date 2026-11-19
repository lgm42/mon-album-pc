namespace MonAlbumPc.Core.Import;

/// <summary>
/// Trash of an album folder, in <c>{album}\.monalbumpc\corbeille</c>. The Windows Recycle Bin does not exist on network
/// shares (a "recycled" file is simply erased), so files of such albums are moved here instead, keeping their path.
/// The import history is left alone: a deleted photo is never imported again.
/// </summary>
public static class AlbumTrash
{
    public static string Folder(string albumRoot) => Path.Combine(albumRoot, DestinationLayout.WorkFolderName, "corbeille");

    /// <summary>True for <c>\\server\share</c> paths and mapped network drives, where there is no Recycle Bin.</summary>
    public static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }
        return OperatingSystem.IsWindows() && Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network;
    }

    /// <summary>Moves <paramref name="path"/> to the trash of its album, with its tag sidecar, and returns where it went.</summary>
    public static string Move(string albumRoot, string path)
    {
        var target = Unique(Path.Combine(Folder(albumRoot), Path.GetRelativePath(albumRoot, path)));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(path, target);
        var sidecar = Media.UserTags.SidecarPath(path);
        if (File.Exists(sidecar))
        {
            try
            {
                File.Move(sidecar, Media.UserTags.SidecarPath(target), overwrite: true);
            }
            catch (IOException)
            {
                // The photo is in the trash: a sidecar left behind only holds its tags.
            }
        }
        return target;
    }

    /// <summary>Number of files in the trash of the album and their total size; zero when it does not exist.</summary>
    public static (int Files, long Bytes) Measure(string albumRoot)
    {
        var folder = Folder(albumRoot);
        if (!Directory.Exists(folder))
        {
            return (0, 0);
        }
        var files = new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }).ToList();
        return (files.Count, files.Sum(f => f.Length));
    }

    /// <summary>Erases the trash of the album for good. The import history keeps the files: they are not imported again.</summary>
    public static void Empty(string albumRoot)
    {
        var folder = Folder(albumRoot);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
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
