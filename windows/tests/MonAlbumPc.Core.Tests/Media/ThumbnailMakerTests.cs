using System.Diagnostics;
using ImageMagick;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class ThumbnailMakerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Root => _temp.Combine("Nico");

    [Fact]
    public void Photo_becomes_a_small_jpeg_without_metadata()
    {
        using var source = new MagickImage(MagickColors.Green, 1600, 1200);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, "2024:05:12 10:30:15");
        source.SetProfile(exif);
        source.Format = MagickFormat.Jpeg;
        var photo = _temp.WriteFile(@"Nico\2024\2024-05\IMG_1.jpg", source.ToByteArray());

        var thumbnail = ThumbnailMaker.Make(Root, photo);

        Assert.Equal(ThumbnailStore.PathFor(Root, photo), thumbnail);
        Assert.Equal(thumbnail, ThumbnailStore.Fresh(Root, photo));
        using var image = new MagickImage(thumbnail);
        Assert.Equal(MagickFormat.Jpeg, image.Format);
        Assert.Equal((640u, 480u), (image.Width, image.Height));
        Assert.Null(image.GetExifProfile());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(thumbnail)!)); // no temporary file left
    }

    [Fact]
    public void Exif_orientation_is_applied_to_the_pixels()
    {
        // Stored landscape, red on the left: shown upright (orientation 6, a quarter turn clockwise), red is on top.
        using var source = new MagickImage(MagickColors.Blue, 1280, 640);
        source.Draw(new ImageMagick.Drawing.Drawables().FillColor(MagickColors.Red).Rectangle(0, 0, 639, 639));
        source.Orientation = Orientation.RightTop;
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, (ushort)6);
        source.SetProfile(exif);
        source.Format = MagickFormat.Jpeg;
        var photo = _temp.WriteFile(@"Nico\IMG_1.jpg", source.ToByteArray());
        Assert.Equal(6, ExifOrientation.Read(photo));

        using var image = new MagickImage(ThumbnailMaker.Make(Root, photo));

        Assert.Equal((320u, 640u), (image.Width, image.Height));
        Assert.Equal(1, ExifOrientation.Read(ThumbnailStore.PathFor(Root, photo)));
        using var pixels = image.GetPixels();
        Assert.True(IsMostly(pixels.GetPixel(160, 100)!.ToColor()!, red: true));
        Assert.True(IsMostly(pixels.GetPixel(160, 540)!.ToColor()!, red: false));
    }

    [Fact]
    public void Small_photo_is_not_enlarged_and_transparency_turns_white()
    {
        using var source = new MagickImage(MagickColors.Transparent, 40, 20) { Format = MagickFormat.Png };
        var photo = _temp.WriteFile(@"Nico\logo.png", source.ToByteArray());

        using var image = new MagickImage(ThumbnailMaker.Make(Root, photo));

        Assert.Equal((40u, 20u), (image.Width, image.Height));
        Assert.Equal(MagickColors.White, image.GetPixels().GetPixel(10, 10)!.ToColor());
    }

    [Fact]
    public void Heic_photo_gets_a_thumbnail()
    {
        // Generated with pillow-heif: 64x48.
        var photo = _temp.WriteFile(@"Nico\IMG_1.heic", File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic")));

        using var image = new MagickImage(ThumbnailMaker.Make(Root, photo));

        Assert.Equal(MagickFormat.Jpeg, image.Format);
        Assert.Equal((64u, 48u), (image.Width, image.Height));
    }

    [Fact]
    public void Photo_dated_in_the_future_still_has_a_fresh_thumbnail()
    {
        var photo = _temp.WriteFile(@"Nico\IMG_1.jpg", TestImages.Jpeg(), DateTime.Now.AddDays(3));

        ThumbnailMaker.Make(Root, photo);

        Assert.NotNull(ThumbnailStore.Fresh(Root, photo));
    }

    [Fact]
    public void Undecodable_photo_throws_and_leaves_nothing()
    {
        var photo = _temp.WriteFile(@"Nico\IMG_1.jpg", [1, 2, 3]);

        Assert.ThrowsAny<MagickException>(() => ThumbnailMaker.Make(Root, photo));
        Assert.False(Directory.Exists(ThumbnailStore.Folder(Root)) && Directory.EnumerateFiles(ThumbnailStore.Folder(Root), "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void Videos_need_ffmpeg() =>
        Assert.Equal(ThumbnailMaker.Ffmpeg is not null, ThumbnailMaker.CanMake("VID_1.mp4"));

    [FfmpegTheory]
    [InlineData(3.0)]
    [InlineData(0.4)] // shorter than the usual frame position
    public void Video_gives_a_frame(double seconds)
    {
        var video = _temp.Combine("Nico", "VID_1.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(video)!);
        RunFfmpeg("-f", "lavfi", "-i", $"testsrc=duration={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}:size=1280x720:rate=10",
            "-c:v", "mpeg4", video);

        using var image = new MagickImage(ThumbnailMaker.Make(Root, video));

        Assert.Equal(MagickFormat.Jpeg, image.Format);
        Assert.Equal((640u, 360u), (image.Width, image.Height));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(ThumbnailStore.PathFor(Root, video))!));
    }

    private static bool IsMostly(IMagickColor<byte> color, bool red) =>
        red ? color.R > 200 && color.B < 60 : color.B > 200 && color.R < 60;

    private static void RunFfmpeg(params string[] arguments)
    {
        var start = new ProcessStartInfo(ThumbnailMaker.Ffmpeg!) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in (string[])["-hide_banner", "-loglevel", "error", "-y", .. arguments])
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
    }
}

/// <summary>A test that needs the ffmpeg program, skipped where it is not installed.</summary>
public sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute()
    {
        if (ThumbnailMaker.Ffmpeg is null)
        {
            Skip = "ffmpeg is not on the PATH";
        }
    }
}
