using ImageMagick;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
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
    private readonly MediaIndex _media;
    private readonly Album _album;
    private readonly Guid _device = Guid.NewGuid();

    public ImportServiceTests()
    {
        var database = new Database(_temp.Combine("catalog.db"));
        _albums = new AlbumStore(database);
        _history = new ImportHistory(database);
        _media = new MediaIndex(database);
        _service = new ImportService(_albums, _history, new FixedClock(Now), _media);
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

    [Fact]
    public void Records_the_shooting_place_so_the_index_does_not_read_the_file_again()
    {
        var result = _service.Import(Incoming(TestImages.GeotaggedJpeg(43.2965, 5.3698, "2024:05:12 10:30:15"), "IMG_1.jpg"));

        var located = Assert.Single(_media.ListLocated());
        Assert.Equal(result.DestinationPath, located.Path);
        Assert.Equal(new DateTime(2024, 5, 12, 10, 30, 15), located.TakenAt);
        Assert.Equal(43.2965, located.Latitude, 4);
        Assert.Equal(5.3698, located.Longitude, 4);
        Assert.Equal(0, _media.Refresh(_album).Read);
    }

    [Fact]
    public void Photo_without_position_is_recorded_without_one()
    {
        _service.Import(Incoming(TestImages.Jpeg("2024:05:12 10:30:15"), "IMG_1.jpg"));

        Assert.Empty(_media.ListLocated());
        Assert.Equal(0, _media.Refresh(_album).Read);
    }

    [Fact]
    public void Whatsapp_photo_gets_a_keyword_and_is_still_recognized_as_sent_by_the_phone()
    {
        var sent = TestImages.Jpeg("2023:11:26 10:12:00");
        var modified = new DateTimeOffset(2023, 11, 26, 10, 12, 30, TimeSpan.FromHours(1));
        var incoming = Incoming(sent, "IMG-20231126-WA0007.jpg", "WhatsApp Images/IMG-20231126-WA0007.jpg") with { ModifiedAt = modified };

        var saved = _service.Import(incoming).DestinationPath!;

        Assert.Equal(["WhatsApp"], XmpKeywords.Read(saved));
        var stored = File.ReadAllBytes(saved);
        Assert.NotEqual(sent.Length, stored.Length);
        Assert.Equal(sent[^100..], stored[^100..]); // image data untouched
        Assert.Equal(modified.UtcDateTime, File.GetLastWriteTimeUtc(saved));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(saved)!)); // no temporary file left
        Assert.True(_history.Contains(_album.Id, Hashing.Sha256(sent)));
        Assert.True(_history.ContainsNameAndSize(_album.Id, "IMG-20231126-WA0007.jpg", sent.Length));
        Assert.Equal(ImportOutcome.AlreadyPresent, _service.Import(Incoming(sent, "IMG-20231126-WA0007.jpg", "WhatsApp Images/x.jpg")).Outcome);
    }

    [Fact]
    public void Screenshots_are_tagged_by_their_folder_and_whatsapp_by_their_name()
    {
        var screenshot = _service.Import(Incoming(TestImages.Jpeg(seed: 1), "Screenshot_20231126-101010.jpg", "Captures d'écran/s.jpg"));
        var whatsApp = _service.Import(Incoming(TestImages.Jpeg(seed: 2), "IMG-20231126-WA0008.jpg"));

        Assert.Equal(["Capture d'écran"], XmpKeywords.Read(screenshot.DestinationPath!));
        Assert.Equal(["WhatsApp"], XmpKeywords.Read(whatsApp.DestinationPath!));
    }

    [Fact]
    public void Camera_photos_and_videos_are_stored_as_sent()
    {
        var photo = TestImages.Jpeg("2024:05:12 10:30:15");
        var video = "not really a video"u8.ToArray();

        var savedPhoto = _service.Import(Incoming(photo, "IMG_1.jpg", "Appareil photo/IMG_1.jpg")).DestinationPath!;
        var savedVideo = _service.Import(Incoming(video, "VID-20231126-WA0001.mp4", "WhatsApp Video/VID-20231126-WA0001.mp4")).DestinationPath!;

        Assert.Equal(photo, File.ReadAllBytes(savedPhoto));
        Assert.Equal(video, File.ReadAllBytes(savedVideo));
    }

    [Fact]
    public void Converted_heic_is_recognized_by_its_size_on_the_phone()
    {
        var heic = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));

        _service.Import(Incoming(heic, "IMG_1.heic"));

        Assert.True(_history.ContainsNameAndSize(_album.Id, "IMG_1.heic", heic.Length));
    }

    private IncomingFile Incoming(byte[] content, string name, string? sourcePath = null)
    {
        var tempPath = _temp.WriteFile(Path.Combine("uploads", Guid.NewGuid().ToString("N")), content);
        return new IncomingFile(tempPath, Hashing.Sha256(content), name, _album.Id, _device, ImportChannel.Wifi, sourcePath);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
