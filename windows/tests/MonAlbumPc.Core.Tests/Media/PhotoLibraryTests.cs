using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class PhotoLibraryTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Lists_media_by_month_newest_first()
    {
        var album = new Album(Guid.NewGuid(), "Marie", _temp.Combine("Marie"), "#000", DateTimeOffset.Now);
        _temp.WriteFile(@"Marie\2024\2024-05\a.jpg", [1], new DateTime(2024, 5, 3));
        _temp.WriteFile(@"Marie\2024\2024-05\b.mp4", [1, 2], new DateTime(2024, 5, 9));
        _temp.WriteFile(@"Marie\2026\2026-10\c.heic", [1], new DateTime(2026, 10, 1));
        _temp.WriteFile(@"Marie\vacances\d.png", [1], new DateTime(2023, 8, 15));
        _temp.WriteFile(@"Marie\notes.txt", [1]);
        _temp.WriteFile(@"Marie\.monalbumpc\uploads\x.jpg", [1]);

        var items = PhotoLibrary.Scan(album);

        Assert.Equal(["c.heic", "b.mp4", "a.jpg", "d.png"], items.Select(i => Path.GetFileName(i.Path)));
        Assert.Equal(new DateTime(2024, 5, 1), items[1].Month);
        Assert.True(items[1].IsVideo);
        Assert.Equal(new DateTime(2023, 8, 1), items[3].Month);
    }

    [Fact]
    public void Missing_album_folder_gives_nothing() =>
        Assert.Empty(PhotoLibrary.Scan(new Album(Guid.NewGuid(), "X", _temp.Combine("missing"), "#000", DateTimeOffset.Now)));
}
