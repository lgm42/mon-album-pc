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

    /// <summary>Moves <paramref name="path"/> to the trash of its album and returns where it went.</summary>
    public static string Move(string albumRoot, string path)
    {
        var target = Unique(Path.Combine(Folder(albumRoot), Path.GetRelativePath(albumRoot, path)));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(path, target);
        return target;
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
