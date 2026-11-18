using MonAlbumPc.Core.Import;

namespace MonAlbumPc.Core.Media;

/// <summary>
/// Thumbnails prepared next to an album, in <c>{album}\.monalbumpc\miniatures</c>, keeping the path of their photo or
/// video plus ".jpg" (<c>2026\2026-10\IMG_1.heic</c> → <c>2026\2026-10\IMG_1.heic.jpg</c>). The NAS writes them; the
/// viewers read a few kilobytes instead of a whole photo across the network.
/// </summary>
public static class ThumbnailStore
{
    /// <summary>Longest side of a thumbnail, in pixels: enough for the largest tiles on a high-density screen.</summary>
    public const int Size = 640;

    public static string Folder(string albumRoot) => Path.Combine(albumRoot, DestinationLayout.WorkFolderName, "miniatures");

    public static string PathFor(string albumRoot, string mediaPath) =>
        Path.Combine(Folder(albumRoot), Path.GetRelativePath(albumRoot, mediaPath) + ".jpg");

    /// <summary>The thumbnail of <paramref name="mediaPath"/> if it exists and is not older than the file (a rotated photo needs a new one).</summary>
    public static string? Fresh(string albumRoot, string mediaPath)
    {
        var thumbnail = PathFor(albumRoot, mediaPath);
        try
        {
            var made = File.GetLastWriteTimeUtc(thumbnail);
            return File.Exists(thumbnail) && made >= File.GetLastWriteTimeUtc(mediaPath) ? thumbnail : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes the thumbnail of <paramref name="mediaPath"/> after a change that keeps the file date (a rotation keeps it
    /// so the photo stays in place): the NAS makes it again, and meanwhile the viewers read the photo itself.
    /// </summary>
    public static void Forget(string albumRoot, string mediaPath)
    {
        try
        {
            File.Delete(PathFor(albumRoot, mediaPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Out of reach: the old thumbnail stays until the photo changes date.
        }
    }
}
