using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Storage;

/// <summary>A NAS and a PC writing to the same album folder must not import the same photo twice.</summary>
public sealed class AlbumJournalsTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CoreServices _nas;
    private readonly CoreServices _pc;
    private readonly Album _nasAlbum;
    private readonly Album _pcAlbum;

    public AlbumJournalsTests()
    {
        var shared = _temp.Combine("photos", "Nicolas");
        Directory.CreateDirectory(shared);
        _nas = CoreServices.Create(_temp.Combine("nas-config"));
        _pc = CoreServices.Create(_temp.Combine("pc-config"));
        // Each server has its own catalog and album id, pointing at the same folder.
        _nasAlbum = new Album(Guid.NewGuid(), "Nicolas", shared, "#000", DateTimeOffset.Now);
        _pcAlbum = new Album(Guid.NewGuid(), "Nicolas", shared, "#000", DateTimeOffset.Now);
        _nas.Albums.Add(_nasAlbum);
        _pc.Albums.Add(_pcAlbum);
    }

    public void Dispose()
    {
        _nas.Dispose();
        _pc.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void A_photo_imported_by_one_server_is_known_to_the_other()
    {
        var photo = TestImages.Jpeg("2024:05:12 10:30:15");
        var saved = Import(_nas, _nasAlbum, photo, "IMG_1.jpg");
        Assert.Equal(ImportOutcome.Saved, saved.Outcome);

        Assert.True(_pc.History.Contains(_pcAlbum.Id, Hashing.Sha256(photo)));
        Assert.True(_pc.History.ContainsNameAndSize(_pcAlbum.Id, "img_1.jpg", new FileInfo(saved.DestinationPath!).Length));
        Assert.Equal(ImportOutcome.AlreadyPresent, Import(_pc, _pcAlbum, photo, "IMG_1.jpg").Outcome);
        Assert.Single(Directory.GetFiles(_nasAlbum.DestinationRoot, "*.jpg", SearchOption.AllDirectories));
    }

    [Fact]
    public void Each_server_writes_only_its_own_journal()
    {
        Import(_nas, _nasAlbum, TestImages.Jpeg(seed: 1), "A.jpg");
        Import(_pc, _pcAlbum, TestImages.Jpeg(seed: 2), "B.jpg");

        var files = Directory.GetFiles(AlbumJournals.Folder(_nasAlbum.DestinationRoot)).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(new[] { $"{_nas.Identity.PcId}.jsonl", $"{_pc.Identity.PcId}.jsonl" }.Order(), files);
    }

    [Fact]
    public void Imports_made_before_journals_existed_are_exported_once()
    {
        Import(_nas, _nasAlbum, TestImages.Jpeg(seed: 3), "C.jpg");
        var journal = Path.Combine(AlbumJournals.Folder(_nasAlbum.DestinationRoot), $"{_nas.Identity.PcId}.jsonl");
        File.Delete(journal);

        _nas.Journals.ExportMissing(_nas.History);
        _nas.Journals.ExportMissing(_nas.History);

        Assert.Single(File.ReadAllLines(journal));
    }

    [Fact]
    public void Journal_lines_carry_the_shooting_place_when_known()
    {
        Import(_nas, _nasAlbum, TestImages.GeotaggedJpeg(48.8584, 2.2945, seed: 4), "Paris.jpg");
        Import(_nas, _nasAlbum, TestImages.Jpeg(seed: 5), "Nowhere.jpg");

        var lines = File.ReadAllLines(Path.Combine(AlbumJournals.Folder(_nasAlbum.DestinationRoot), $"{_nas.Identity.PcId}.jsonl"));
        Assert.Contains("\"latitude\":48.858", lines[0]);
        Assert.Contains("\"longitude\":2.294", lines[0]);
        Assert.DoesNotContain("latitude", lines[1]);
    }

    [Fact]
    public void Lines_written_before_the_shooting_place_existed_are_still_read()
    {
        var folder = AlbumJournals.Folder(_pcAlbum.DestinationRoot);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{Guid.NewGuid()}.jsonl"),
            """{"sha256":"abc","name":"IMG_1.jpg","size":10,"path":"2024/2024-05/IMG_1.jpg","takenAt":"2024-05-12T10:30:15","device":null,"importedAt":"2026-10-20T08:00:00+02:00"}""" + "\n");

        Assert.True(_pc.History.Contains(_pcAlbum.Id, "abc"));
    }

    [Fact]
    public void Journal_lines_carry_the_phone_folder_when_known()
    {
        Import(_nas, _nasAlbum, TestImages.Jpeg(seed: 6), "A.jpg", "Appareil photo/A.jpg");
        Import(_nas, _nasAlbum, TestImages.Jpeg(seed: 7), "B.jpg");

        var lines = File.ReadAllLines(Path.Combine(AlbumJournals.Folder(_nasAlbum.DestinationRoot), $"{_nas.Identity.PcId}.jsonl"));
        Assert.Contains("\"source\":\"Appareil photo\"", lines[0]);
        Assert.DoesNotContain("source", lines[1]);
        Assert.DoesNotContain("originalSize", lines[0]); // same size on the phone and in the album
    }

    [Fact]
    public void Provenance_comes_from_the_catalog_and_from_the_other_servers_journals()
    {
        var nasPhoto = Import(_nas, _nasAlbum, TestImages.Jpeg(seed: 8), "IMG_1.jpg", "WhatsApp Images/IMG_1.jpg").DestinationPath!;
        var pcPhoto = Import(_pc, _pcAlbum, TestImages.Jpeg(seed: 9), "IMG_2.jpg", "Telegram/IMG_2.jpg").DestinationPath!;
        var folder = AlbumJournals.Folder(_pcAlbum.DestinationRoot);
        File.AppendAllText(Path.Combine(folder, $"{Guid.NewGuid()}.jsonl"),
            """{"sha256":"abc","name":"IMG-20231126-WA0007.jpg","size":10,"path":"2023/2023-11/IMG-20231126-WA0007.jpg","takenAt":null,"device":null,"importedAt":"2026-10-20T08:00:00+02:00"}""" + "\n");

        var provenance = AlbumProvenance.Load(_pc.History, _pcAlbum);

        Assert.Equal(MediaSourceKind.WhatsApp, provenance.Of(nasPhoto).Kind);
        Assert.Equal("Telegram", provenance.Of(pcPhoto).Label);
        // An old line without folder: the name tells.
        Assert.Equal(MediaSourceKind.WhatsApp,
            provenance.Of(Path.Combine(_pcAlbum.DestinationRoot, "2023", "2023-11", "IMG-20231126-WA0007.jpg")).Kind);
    }

    [Fact]
    public void A_converted_photo_is_known_to_the_other_server_by_its_size_on_the_phone()
    {
        var heic = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"));
        var saved = Import(_nas, _nasAlbum, heic, "IMG_1.heic");
        Assert.NotEqual(heic.Length, new FileInfo(saved.DestinationPath!).Length);

        Assert.True(_pc.History.ContainsNameAndSize(_pcAlbum.Id, "IMG_1.heic", heic.Length));
        Assert.True(_nas.History.ContainsNameAndSize(_nasAlbum.Id, "IMG_1.heic", heic.Length));
    }

    private static ImportResult Import(CoreServices core, Album album, byte[] bytes, string name, string? sourcePath = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "monalbumpc-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        File.WriteAllBytes(temp, bytes);
        return core.Import.Import(new IncomingFile(temp, Hashing.Sha256(bytes), name, album.Id, null, ImportChannel.Wifi, sourcePath));
    }
}
