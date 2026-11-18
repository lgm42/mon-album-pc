using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class ThumbnailStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Thumbnail_keeps_the_path_of_its_photo()
    {
        var root = _temp.Combine("Nico");

        var path = ThumbnailStore.PathFor(root, Path.Combine(root, "2026", "2026-10", "IMG_1.heic"));

        Assert.Equal(Path.Combine(root, ".monalbumpc", "miniatures", "2026", "2026-10", "IMG_1.heic.jpg"), path);
    }

    [Fact]
    public void Only_a_thumbnail_newer_than_its_photo_is_used()
    {
        var root = _temp.Combine("Nico");
        var photo = _temp.WriteFile(@"Nico\2026\IMG_1.jpg", [1]);
        Assert.Null(ThumbnailStore.Fresh(root, photo));

        var thumbnail = ThumbnailStore.PathFor(root, photo);
        Directory.CreateDirectory(Path.GetDirectoryName(thumbnail)!);
        File.WriteAllBytes(thumbnail, [2]);
        Assert.Equal(thumbnail, ThumbnailStore.Fresh(root, photo));

        // The photo was rotated after its thumbnail was made.
        File.SetLastWriteTimeUtc(photo, File.GetLastWriteTimeUtc(thumbnail).AddMinutes(1));
        Assert.Null(ThumbnailStore.Fresh(root, photo));
    }
}
