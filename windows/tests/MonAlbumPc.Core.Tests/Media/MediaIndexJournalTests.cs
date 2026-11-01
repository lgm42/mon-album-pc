using System.Text.Json;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Media;

/// <summary>A viewer indexing a library written by a NAS: the journals save opening each file over the network.</summary>
public sealed class MediaIndexJournalTests : IDisposable
{
    private const string RelativePath = "2024/2024-05/IMG_1.jpg";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TempDirectory _temp = new();
    private readonly CoreServices _core;
    private readonly Album _album;
    private readonly string _file;

    public MediaIndexJournalTests()
    {
        _core = CoreServices.Create(_temp.Combine("viewer"));
        _album = new Album(Guid.NewGuid(), "Nicolas", _temp.Combine("library", "Nico"), "#0F6CBD", DateTimeOffset.Now);
        _core.Albums.Add(_album);
        _file = Path.Combine(_album.DestinationRoot, "2024", "2024-05", "IMG_1.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
    }

    public void Dispose()
    {
        _core.Dispose();
        _temp.Dispose();
    }

    private void WriteJournal(long size, double? latitude, double? longitude, Guid? server = null, string? device = "POCO X7 Pro",
        string? takenAt = "2024-05-12T10:30:15")
    {
        var folder = AlbumJournals.Folder(_album.DestinationRoot);
        Directory.CreateDirectory(folder);
        var entry = new JournalEntry(new string('a', 64), "IMG_1.jpg", size, RelativePath, takenAt, device, DateTimeOffset.Now, latitude, longitude);
        File.WriteAllText(Path.Combine(folder, (server ?? Guid.NewGuid()) + ".jsonl"), JsonSerializer.Serialize(entry, Json) + "\n");
    }

    [Fact]
    public void A_journaled_position_is_taken_without_opening_the_file()
    {
        File.WriteAllBytes(_file, [1, 2, 3, 4]);
        WriteJournal(4, 48.8584, 2.2945);

        // Locked: reading it would fail, and the file would then be skipped until the next refresh.
        using (new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _core.Media.Refresh(_album);
        }

        var media = Assert.Single(_core.Media.ListLocated());
        Assert.Equal(_file, media.Path);
        Assert.Equal(48.8584, media.Latitude, 4);
        Assert.Equal(2.2945, media.Longitude, 4);
        Assert.Equal(new DateTime(2024, 5, 12, 10, 30, 15), media.TakenAt);
    }

    [Fact]
    public void A_journal_without_date_falls_back_on_the_file_name()
    {
        File.WriteAllBytes(_file, [1, 2, 3, 4]);
        var renamed = Path.Combine(Path.GetDirectoryName(_file)!, "IMG_20240512_103015.jpg");
        File.Move(_file, renamed);
        var folder = AlbumJournals.Folder(_album.DestinationRoot);
        Directory.CreateDirectory(folder);
        var entry = new JournalEntry(new string('a', 64), "x.jpg", 4, "2024/2024-05/IMG_20240512_103015.jpg", null, null, DateTimeOffset.Now, 1, 2);
        File.WriteAllText(Path.Combine(folder, Guid.NewGuid() + ".jsonl"), JsonSerializer.Serialize(entry, Json) + "\n");

        _core.Media.Refresh(_album);

        Assert.Equal(new DateTime(2024, 5, 12, 10, 30, 15), Assert.Single(_core.Media.ListLocated()).TakenAt);
    }

    [Fact]
    public void A_file_of_another_size_is_read_because_it_is_not_the_journaled_one()
    {
        File.WriteAllBytes(_file, TestImages.GeotaggedJpeg(45.764, 4.8357, "2025:01:02 08:00:00"));
        WriteJournal(4, 48.8584, 2.2945);

        _core.Media.Refresh(_album);

        Assert.Equal(45.764, Assert.Single(_core.Media.ListLocated()).Latitude, 4);
    }

    [Fact]
    public void A_journal_line_without_position_does_not_spare_reading_the_file()
    {
        var photo = TestImages.GeotaggedJpeg(45.764, 4.8357, "2025:01:02 08:00:00");
        File.WriteAllBytes(_file, photo);
        WriteJournal(photo.Length, null, null);

        _core.Media.Refresh(_album);

        Assert.Equal(45.764, Assert.Single(_core.Media.ListLocated()).Latitude, 4);
    }

    [Fact]
    public void This_servers_own_journal_is_not_a_source()
    {
        var photo = TestImages.Jpeg("2025:01:02 08:00:00");
        File.WriteAllBytes(_file, photo);
        WriteJournal(photo.Length, 48.8584, 2.2945, _core.Identity.PcId);

        _core.Media.Refresh(_album);

        Assert.Empty(_core.Media.ListLocated());
    }

    [Fact]
    public void The_phone_of_origin_comes_from_the_journal()
    {
        File.WriteAllBytes(_file, [1, 2, 3, 4]);
        WriteJournal(4, null, null);

        Assert.Equal("POCO X7 Pro", _core.Journals.DeviceOf(_album, _file));
        Assert.Null(_core.Journals.DeviceOf(_album, Path.Combine(_album.DestinationRoot, "autre.jpg")));
    }
}
