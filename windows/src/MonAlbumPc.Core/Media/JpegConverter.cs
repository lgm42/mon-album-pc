using ImageMagick;

namespace MonAlbumPc.Core.Media;

public static class JpegConverter
{
    public const uint Quality = 92;

    /// <summary>Converts a HEIC/HEIF photo to JPEG, keeping its metadata (date, GPS…).</summary>
    public static void Convert(string source, string destination)
    {
        using var image = new MagickImage(source);
        // libheif already applies the HEIF rotation while decoding: the pixels are upright.
        image.Orientation = Orientation.TopLeft;
        if (image.GetExifProfile() is { } exif)
        {
            exif.SetValue(ExifTag.Orientation, (ushort)1);
            image.SetProfile(exif);
        }
        image.Quality = Quality;
        image.Format = MagickFormat.Jpeg;
        image.Write(destination);
    }
}
