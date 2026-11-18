namespace MonAlbumPc.Core.Media;

/// <summary>
/// Paths of album photos as a web page names them: relative to the album folder, with '/' separators
/// (<c>2026/2026-11/IMG_1.jpg</c>). <see cref="Resolve"/> turns such a path back into a file, refusing anything that
/// would reach outside the album or into its hidden folders.
/// </summary>
public static class AlbumMediaPath
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly char[] Separators = OperatingSystem.IsWindows() ? ['/', '\\'] : ['/'];

    public static string Relative(string albumRoot, string fullPath) =>
        Path.GetRelativePath(albumRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// The photo or video at <paramref name="relativePath"/> inside <paramref name="albumRoot"/>, or null when the path is
    /// not a media file of the album: empty, absolute, with <c>.</c> or <c>..</c> segments, through a hidden folder (such as
    /// <c>.monalbumpc</c>, trash included), not a photo or video, missing, or behind a link leading out of the album.
    /// </summary>
    public static string? Resolve(string albumRoot, string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || relativePath.Contains('\0') || !MediaTypes.IsMedia(relativePath))
        {
            return null;
        }
        var segments = relativePath.Split(Separators);
        // Empty segments ("/x", "a//b"), ".", "..", and hidden names (".monalbumpc", ".x.jpg") are never served.
        if (segments.Any(s => s.Length == 0 || s[0] == '.' || (OperatingSystem.IsWindows() && s.Contains(':'))))
        {
            return null;
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(albumRoot));
        var full = Path.GetFullPath(Path.Combine([root, .. segments]));
        if (!IsInside(root, full) || !File.Exists(full))
        {
            return null;
        }
        return LeavesRoot(root, segments) ? null : full;
    }

    private static bool IsInside(string root, string path) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    /// <summary>True when a folder or the file on the way is a link whose target is outside the album.</summary>
    private static bool LeavesRoot(string root, string[] segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            try
            {
                if (entry.LinkTarget is not null
                    && (entry.ResolveLinkTarget(returnFinalTarget: true) is not { } target || !IsInside(root, Path.GetFullPath(target.FullName))))
                {
                    return true;
                }
            }
            catch (IOException)
            {
                return true; // a link loop or an unreadable link
            }
        }
        return false;
    }
}
