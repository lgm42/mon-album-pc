using ImageMagick;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Import;

public sealed class ImportServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly TempDirectory _temp = new();
    private readonly AlbumStore _albums;
    private readonly ImportHistory _history;
    private readonly ImportService _service;
    private readonly Album _album;
    private readonly Guid _device = Guid.NewGuid();

    public ImportServiceTests()
    {
        var database = new Database(_temp.Combine("catalog.db"));
        _albums = new AlbumStore(database);
        _history = new ImportHistory(database);
        _service = new ImportService(_albums, _history, new FixedClock(Now));
        _album = new Album(Guid.NewGuid(), "Marie", _temp.Combine("Photos", "Marie"), "#3D4FD1", Now);
        _albums.Add(_album);
        Directory.CreateDirectory(_album.DestinationRoot);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Saves_photo_in_its_shooting_month_and_records_it()
    {
        var incoming = Incoming(TestImages.Jpeg("2024:05:12 10:30:15"), "IMG_1.jpg");

        var result = _service.Import(incoming);

        Assert.Equal(ImportOutcome.Saved, result.Outcome);
        Assert.Equal(Path.Combine(_album.DestinationRoot, "2024", "2024-05", "IMG_1.jpg"), result.DestinationPath);
        Assert.False(File.Exists(incoming.TempPath));
        Assert.True(_history.Contains(_album.Id, incoming.Sha256));
        Assert.Equal(1, _history.CountByDevice(_device));
    }

    [Fact]
    public void Keeps_the_original_modification_date()
    {
        var modified = new DateTimeOffset(2024, 5, 12, 10, 31, 0, TimeSpan.FromHours(2));
        var result = _service.Import(Incoming(TestImages.Jpeg("2024:05:12 10:30:15"), "IMG_1.jpg") with { ModifiedAt = modified });

        Assert.Equal(modified.UtcDateTime, File.GetLastWriteTimeUtc(result.DestinationPath!));
    }

    [Fact]
    public void Same_bytes_are_never_imported_twice()
    {
        var bytes = TestImages.Jpeg("2024:05:12 10:30:15");
        _service.Import(Incoming(bytes, "IMG_1.jpg"));

        var again = Incoming(bytes, "renamed.jpg");
        var result = _service.Import(again);

        Assert.Equal(ImportOutcome.AlreadyPresent, result.Outcome);
        Assert.False(File.Exists(again.TempPath));
        Assert.Single(Directory.GetFiles(_album.DestinationRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Different_content_with_same_name_gets_a_suffix()
    {
        _service.Import(Incoming(TestImages.Jpeg("2024:05:12 10:30:15", seed: 1), "IMG_1.jpg"));
        var result = _service.Import(Incoming(TestImages.Jpeg("2024:05:12 11:00:00", seed: 2), "IMG_1.jpg"));

        Assert.Equal(Path.Combine(_album.DestinationRoot, "2024", "2024-05", "IMG_1_1.jpg"), result.DestinationPath);
    }

    [Fact]
    public void Identical_file_already_in_the_folder_is_not_duplicated()
    {
        var bytes = TestImages.Jpeg("2024:05:12 10:30:15");
        _temp.WriteFile(Path.Combine("Photos", "Marie", "2024", "2024-05", "IMG_1.jpg"), bytes);

        var result = _service.Import(Incoming(bytes, "IMG_1.jpg"));

        Assert.Equal(ImportOutcome.AlreadyPresent, result.Outcome);
    }

    [Fact]
    public void Indexed_file_with_same_name_and_date_counts_as_present()
    {
        _history.UpsertIndexEntry(new IndexEntry(_album.Id, @"old\IMG_1.jpg", "other-hash", 1, "IMG_1.jpg", new DateTime(2024, 5, 12, 10, 30, 15), Now));

        var result = _service.Import(Incoming(TestImages.Jpeg("2024:05:12 10:30:15"), "IMG_1.jpg"));

        Assert.Equal(ImportOutcome.AlreadyPresent, result.Outcome);
    }

    [Fact]
    public void Hash_mismatch_is_rejected_and_the_upload_dropped()
    {
        var incoming = Incoming(TestImages.Jpeg(), "IMG_1.jpg") with { Sha256 = new string('0', 64) };

        Assert.Throws<HashMismatchException>(() => _service.Import(incoming));
        Assert.False(File.Exists(incoming.TempPath));
    }

    [Fact]
    public void Missing_destination_keeps_the_upload_for_later()
    {
        Directory.Delete(_album.DestinationRoot);
        var incoming = Incoming(TestImages.Jpeg(), "IMG_1.jpg");

        Assert.Throws<DestinationUnavailableException>(() => _service.Import(incoming));
        Assert.True(File.Exists(incoming.TempPath));
    }

    [Fact]
    public void Heic_is_converted_to_jpeg_and_recorded_with_the_original_hash()
    {
        // Generated with pillow-heif: 64x48, EXIF DateTimeOriginal 2024:05:12 10:30:15.
        var heic = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));
        var incoming = Incoming(heic, "IMG_1.heic");

        var result = _service.Import(incoming);

        Assert.Equal(Path.Combine(_album.DestinationRoot, "2024", "2024-05", "IMG_1.jpg"), result.DestinationPath);
        using var image = new MagickImage(result.DestinationPath!);
        Assert.Equal(MagickFormat.Jpeg, image.Format);
        Assert.Equal(new DateTime(2024, 5, 12, 10, 30, 15), MonAlbumPc.Core.Media.TakenDateResolver.ReadMetadataDate(result.DestinationPath!));
        Assert.True(_history.Contains(_album.Id, incoming.Sha256));
    }

    private IncomingFile Incoming(byte[] content, string name)
    {
        var tempPath = _temp.WriteFile(Path.Combine("uploads", Guid.NewGuid().ToString("N")), content);
        return new IncomingFile(tempPath, Hashing.Sha256(content), name, _album.Id, _device, ImportChannel.Wifi);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
