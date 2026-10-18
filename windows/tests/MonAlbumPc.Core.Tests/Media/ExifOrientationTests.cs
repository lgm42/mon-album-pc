using ImageMagick;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class ExifOrientationTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Rotating_a_jpeg_only_changes_its_orientation_tag()
    {
        var path = _temp.WriteFile("a.jpg", Jpeg(orientation: 1));
        var before = File.ReadAllBytes(path);

        ExifOrientation.Rotate(path, clockwise: true);

        Assert.Equal(6, ExifOrientation.Read(path));
        var after = File.ReadAllBytes(path);
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(1, before.Zip(after).Count(p => p.First != p.Second)); // one byte of the tag, nothing else
    }

    [Theory]
    [InlineData(1, true, 6)]
    [InlineData(6, true, 3)]
    [InlineData(3, true, 8)]
    [InlineData(8, true, 1)]
    [InlineData(1, false, 8)]
    [InlineData(6, false, 1)]
    public void Quarter_turns_cycle_through_orientations(ushort start, bool clockwise, ushort expected)
    {
        var path = _temp.WriteFile("a.jpg", Jpeg(start));
        ExifOrientation.Rotate(path, clockwise);
        Assert.Equal(expected, ExifOrientation.Read(path));
    }

    [Fact]
    public void Photos_without_exif_get_their_pixels_rotated()
    {
        using var image = new MagickImage(MagickColors.Red, 40, 20) { Format = MagickFormat.Png };
        var path = _temp.WriteFile("a.png", image.ToByteArray());

        ExifOrientation.Rotate(path, clockwise: true);

        using var rotated = new MagickImage(path);
        Assert.Equal((20u, 40u), (rotated.Width, rotated.Height));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(6, 90)]
    [InlineData(3, 180)]
    [InlineData(8, 270)]
    public void Orientation_maps_to_display_rotation(ushort orientation, int degrees) =>
        Assert.Equal(degrees, ExifOrientation.Degrees(orientation));

    private static byte[] Jpeg(ushort orientation)
    {
        // Magick writes its own Orientation property into the EXIF profile when saving.
        using var image = new MagickImage(MagickColors.Blue, 40, 20) { Orientation = (Orientation)orientation };
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, orientation);
        exif.SetValue(ExifTag.DateTimeOriginal, "2024:05:12 10:30:15");
        image.SetProfile(exif);
        image.Format = MagickFormat.Jpeg;
        return image.ToByteArray();
    }
}
