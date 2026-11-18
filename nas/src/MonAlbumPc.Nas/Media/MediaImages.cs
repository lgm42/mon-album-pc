using ImageMagick;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Nas.Media;

/// <summary>
/// Photos resized on the fly for the web pages, in memory only: thumbnails while the background generator has not made
/// them yet, and JPEG versions of what browsers cannot show (HEIC/HEIF, DNG). A few at a time, to spare the NAS.
/// </summary>
public sealed class MediaImages
{
    /// <summary>Longest side of a photo shown full screen.</summary>
    public const int PreviewSize = 2560;

    private readonly SemaphoreSlim _gate = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));

    /// <summary>Formats every browser displays as they are.</summary>
    public static bool BrowserShows(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp";

    public Task<byte[]?> ThumbnailAsync(string path, CancellationToken cancellation) =>
        RenderAsync(path, ThumbnailStore.Size, 80, cancellation);

    public Task<byte[]?> PreviewAsync(string path, CancellationToken cancellation) =>
        RenderAsync(path, PreviewSize, 88, cancellation);

    /// <summary>Width and height of a photo as displayed (EXIF rotation applied), or null when unreadable.</summary>
    public static (uint Width, uint Height)? Dimensions(string path)
    {
        try
        {
            var info = new MagickImageInfo(path);
            var turned = IsJpeg(path) && ExifOrientation.Degrees(ExifOrientation.Read(path)) % 180 != 0;
            return turned ? (info.Height, info.Width) : (info.Width, info.Height);
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<byte[]?> RenderAsync(string path, int size, uint quality, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            return await Task.Run(() => Render(path, (uint)size, quality), cancellation);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static byte[]? Render(string path, uint size, uint quality)
    {
        try
        {
            var settings = new MagickReadSettings();
            if (IsJpeg(path))
            {
                // Let libjpeg decode at a reduced scale: much faster and lighter than a full 12 MP decode.
                settings.SetDefine(MagickFormat.Jpeg, "size", $"{size}x{size}");
            }
            using var image = new MagickImage(path, settings);
            if (MediaTypes.NeedsJpegConversion(path))
            {
                // libheif already applies the HEIF rotation while decoding (see JpegConverter).
                image.Orientation = Orientation.TopLeft;
            }
            else
            {
                image.AutoOrient();
            }
            // Wide-gamut photos (Display P3…) keep their colors once the profile is stripped.
            if (image.GetColorProfile() is not null)
            {
                image.TransformColorSpace(ColorProfiles.SRGB);
            }
            image.Thumbnail(new MagickGeometry(size, size) { Greater = true });
            if (image.HasAlpha)
            {
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
            }
            image.Strip();
            image.Quality = quality;
            image.Format = MagickFormat.Jpeg;
            return image.ToByteArray();
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsJpeg(string path) => Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg";
}
