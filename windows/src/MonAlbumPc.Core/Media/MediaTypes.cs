namespace MonAlbumPc.Core.Media;

public static class MediaTypes
{
    private static readonly HashSet<string> Photos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".bmp", ".dng",
    };

    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".3gp", ".mov", ".mkv", ".webm", ".avi", ".m4v",
    };

    public static bool IsPhoto(string path) => Photos.Contains(Path.GetExtension(path));

    public static bool IsVideo(string path) => Videos.Contains(Path.GetExtension(path));

    public static bool IsMedia(string path) => IsPhoto(path) || IsVideo(path);

    /// <summary>HEIC/HEIF photos are converted to JPEG on import.</summary>
    public static bool NeedsJpegConversion(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".heic" or ".heif";
}
