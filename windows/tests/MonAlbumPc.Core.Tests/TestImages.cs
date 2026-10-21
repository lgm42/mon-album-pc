using ImageMagick;

namespace MonAlbumPc.Core.Tests;

/// <summary>Small generated images for tests.</summary>
public static class TestImages
{
    public static byte[] Jpeg(string? dateTimeOriginal = null, int seed = 0, MagickFormat format = MagickFormat.Jpeg) =>
        Jpeg(dateTimeOriginal, seed, format, null);

    /// <summary>A JPEG geotagged like a phone does it: degrees/minutes/seconds rationals plus N/S and E/W references.</summary>
    public static byte[] GeotaggedJpeg(double latitude, double longitude, string? dateTimeOriginal = null, int seed = 0) =>
        Jpeg(dateTimeOriginal, seed, MagickFormat.Jpeg, (latitude, longitude));

    private static byte[] Jpeg(string? dateTimeOriginal, int seed, MagickFormat format, (double Lat, double Lon)? gps)
    {
        using var image = new MagickImage(new MagickColor((byte)(seed * 40), 120, 200), 64, 48);
        if (dateTimeOriginal is not null || gps is not null)
        {
            var exif = new ExifProfile();
            if (dateTimeOriginal is not null)
            {
                exif.SetValue(ExifTag.DateTimeOriginal, dateTimeOriginal);
            }
            if (gps is { } position)
            {
                exif.SetValue(ExifTag.GPSLatitude, Dms(position.Lat));
                exif.SetValue(ExifTag.GPSLatitudeRef, position.Lat < 0 ? "S" : "N");
                exif.SetValue(ExifTag.GPSLongitude, Dms(position.Lon));
                exif.SetValue(ExifTag.GPSLongitudeRef, position.Lon < 0 ? "W" : "E");
            }
            image.SetProfile(exif);
        }
        image.Format = format;
        return image.ToByteArray();
    }

    private static Rational[] Dms(double value)
    {
        value = Math.Abs(value);
        var degrees = Math.Floor(value);
        var minutes = Math.Floor((value - degrees) * 60);
        var seconds = ((value - degrees) * 60 - minutes) * 60;
        return [new Rational((uint)degrees, 1), new Rational((uint)minutes, 1), new Rational((uint)Math.Round(seconds * 10000), 10000)];
    }
}
