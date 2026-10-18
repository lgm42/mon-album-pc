using ImageMagick;

namespace MonAlbumPc.Core.Tests;

/// <summary>Small generated images for tests.</summary>
public static class TestImages
{
    public static byte[] Jpeg(string? dateTimeOriginal = null, int seed = 0, MagickFormat format = MagickFormat.Jpeg)
    {
        using var image = new MagickImage(new MagickColor((byte)(seed * 40), 120, 200), 64, 48);
        if (dateTimeOriginal is not null)
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.DateTimeOriginal, dateTimeOriginal);
            image.SetProfile(exif);
        }
        image.Format = format;
        return image.ToByteArray();
    }
}
