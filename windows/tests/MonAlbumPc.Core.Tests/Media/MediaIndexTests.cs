using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class MediaIndexTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly AlbumStore _albums;
    private readonly MediaIndex _index;
    private readonly Album _album;

    public MediaIndexTests()
    {
        var database = new Database(_temp.Combine("catalog.db"));
        _albums = new AlbumStore(database);
        _index = new MediaIndex(database);
        _album = new Album(Guid.NewGuid(), "Marie", _temp.Combine("Photos", "Marie"), "#8764B8", DateTimeOffset.Now);
        _albums.Add(_album);
        Directory.CreateDirectory(_album.DestinationRoot);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Lists_located_media_newest_first_with_full_paths()
    {
        var paris = Write(@"2024\2024-05\paris.jpg", TestImages.GeotaggedJpeg(48.8584, 2.2945, "2024:05:12 10:30:15"));
        var lyon = Write(@"2025\2025-01\lyon.jpg", TestImages.GeotaggedJpeg(45.764, 4.8357, "2025:01:02 08:00:00", seed: 1));
        Write(@"2025\2025-01\nogps.jpg", TestImages.Jpeg("2025:01:03 08:00:00", seed: 2));

        var result = _index.Refresh(_album);

        Assert.Equal(3, result.Read);
        var located = _index.ListLocated();
        Assert.Equal([lyon, paris], located.Select(m => m.Path));
        Assert.Equal(new DateTime(2025, 1, 2, 8, 0, 0), located[0].TakenAt);
        Assert.Equal(45.764, located[0].Latitude, 4);
        Assert.All(located, m => Assert.Equal(_album.Id, m.AlbumId));
        Assert.Empty(_index.ListLocated(Guid.NewGuid()));
    }

    [Fact]
    public void Unchanged_files_are_not_read_again()
    {
        Write(@"2024\2024-05\a.jpg", TestImages.GeotaggedJpeg(48.8584, 2.2945, seed: 1));
        Write(@"2024\2024-05\b.jpg", TestImages.Jpeg(seed: 2));
        _index.Refresh(_album);

        var again = _index.Refresh(_album);

        Assert.Equal(new MediaIndexResult(0, 0), again);
        Assert.False(again.Changed);
    }

    [Fact]
    public void Changed_files_are_read_again()
    {
        var path = Write(@"2024\2024-05\a.jpg", TestImages.Jpeg(seed: 1));
        _index.Refresh(_album);
        Assert.Empty(_index.ListLocated());

        File.WriteAllBytes(path, TestImages.GeotaggedJpeg(43.2965, 5.3698, seed: 3));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(1, _index.Refresh(_album).Read);
        Assert.Single(_index.ListLocated());
    }

    [Fact]
    public void Deleted_files_are_removed()
    {
        var path = Write(@"2024\2024-05\a.jpg", TestImages.GeotaggedJpeg(48.8584, 2.2945));
        _index.Refresh(_album);

        File.Delete(path);

        Assert.Equal(new MediaIndexResult(0, 1), _index.Refresh(_album));
        Assert.Empty(_index.ListLocated());
    }

    [Fact]
    public void An_unreachable_album_folder_keeps_its_entries()
    {
        Write(@"2024\2024-05\a.jpg", TestImages.GeotaggedJpeg(48.8584, 2.2945));
        _index.Refresh(_album);

        Directory.Move(_album.DestinationRoot, _album.DestinationRoot + "-unplugged");

        Assert.False(_index.Refresh(_album).Changed);
        Assert.Single(_index.ListLocated());
    }

    [Fact]
    public void Work_folder_and_other_files_are_skipped()
    {
        Write(Path.Combine(DestinationLayout.WorkFolderName, "thumbs", "x.jpg"), TestImages.GeotaggedJpeg(48.8584, 2.2945));
        Write(@"2024\notes.txt", [1, 2, 3]);

        Assert.Equal(0, _index.Refresh(_album).Read);
    }

    [Fact]
    public void Refresh_can_be_cancelled()
    {
        Write(@"2024\2024-05\a.jpg", TestImages.Jpeg());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => _index.Refresh(_album, cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Deleting_the_album_forgets_its_media()
    {
        Write(@"2024\2024-05\a.jpg", TestImages.GeotaggedJpeg(48.8584, 2.2945));
        _index.Refresh(_album);

        _albums.Delete(_album.Id);

        Assert.Empty(_index.ListLocated());
    }

    private string Write(string relative, byte[] content) => _temp.WriteFile(Path.Combine("Photos", "Marie", relative), content);
}
