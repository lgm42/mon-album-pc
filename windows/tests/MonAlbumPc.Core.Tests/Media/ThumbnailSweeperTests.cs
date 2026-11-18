using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class ThumbnailSweeperTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ThumbnailSweeper _sweeper = new(parallelism: 2);
    private readonly Album _album;

    public ThumbnailSweeperTests() =>
        _album = new Album(Guid.NewGuid(), "Nico", _temp.Combine("Nico"), Album.Palette[0], DateTimeOffset.Now);

    public void Dispose() => _temp.Dispose();

    private ThumbnailSweepResult Sweep() => _sweeper.Sweep([_album], CancellationToken.None);

    [Fact]
    public void Makes_the_missing_thumbnails_once()
    {
        var first = _temp.WriteFile(@"Nico\2024\2024-05\IMG_1.jpg", TestImages.Jpeg(seed: 1));
        var second = _temp.WriteFile(@"Nico\copied by hand\IMG_2.png", TestImages.Jpeg(seed: 2, format: ImageMagick.MagickFormat.Png));

        Assert.Equal(new ThumbnailSweepResult(2, 0, 0, 0, null), Sweep());
        Assert.NotNull(ThumbnailStore.Fresh(_album.DestinationRoot, first));
        Assert.NotNull(ThumbnailStore.Fresh(_album.DestinationRoot, second));

        Assert.Equal(new ThumbnailSweepResult(0, 0, 0, 2, null), Sweep());
    }

    [Fact]
    public void Remakes_the_thumbnail_of_a_changed_photo()
    {
        var photo = _temp.WriteFile(@"Nico\IMG_1.jpg", TestImages.Jpeg());
        Sweep();
        File.SetLastWriteTimeUtc(photo, File.GetLastWriteTimeUtc(ThumbnailStore.PathFor(_album.DestinationRoot, photo)).AddMinutes(1));

        Assert.Equal(1, Sweep().Made);
        Assert.NotNull(ThumbnailStore.Fresh(_album.DestinationRoot, photo));
    }

    [Fact]
    public void Removes_the_thumbnail_of_a_photo_moved_to_the_trash()
    {
        var photo = _temp.WriteFile(@"Nico\2024\2024-05\IMG_1.jpg", TestImages.Jpeg());
        Sweep();
        MonAlbumPc.Core.Import.AlbumTrash.Move(_album.DestinationRoot, photo);

        Assert.Equal(new ThumbnailSweepResult(0, 1, 0, 0, null), Sweep());
        Assert.Empty(Directory.EnumerateFiles(ThumbnailStore.Folder(_album.DestinationRoot), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Removes_temporary_files_left_by_an_interrupted_run()
    {
        var recent = _temp.WriteFile(@"Nico\.monalbumpc\miniatures\IMG_1.jpg.jpg.1.tmp", [1]);
        var abandoned = _temp.WriteFile(@"Nico\.monalbumpc\miniatures\IMG_2.jpg.jpg.2.tmp", [1], DateTime.Now.AddHours(-2));

        Assert.Equal(1, Sweep().Removed);
        Assert.True(File.Exists(recent));
        Assert.False(File.Exists(abandoned));
    }

    [Fact]
    public void An_undecodable_photo_is_reported_once_until_it_changes()
    {
        var broken = _temp.WriteFile(@"Nico\IMG_1.jpg", [1, 2, 3]);
        _temp.WriteFile(@"Nico\IMG_2.jpg", TestImages.Jpeg());

        var first = Sweep();
        Assert.Equal((1, 1), (first.Made, first.Failed));
        Assert.StartsWith("IMG_1.jpg : ", first.FirstError);

        Assert.Equal(new ThumbnailSweepResult(0, 0, 0, 1, null), Sweep());

        File.WriteAllBytes(broken, TestImages.Jpeg());
        File.SetLastWriteTimeUtc(broken, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(1, Sweep().Made);
    }

    [Fact]
    public void Videos_wait_for_ffmpeg()
    {
        _temp.WriteFile(@"Nico\VID_1.mp4", [1, 2, 3]);

        var result = Sweep();

        // Without ffmpeg the video is left alone; with it, these three bytes are no video.
        Assert.Equal(ThumbnailMaker.Ffmpeg is null ? 0 : 1, result.Failed);
    }
}
