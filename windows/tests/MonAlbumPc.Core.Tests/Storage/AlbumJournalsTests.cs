using MonAlbumPc.Core.Import;
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

    private static ImportResult Import(CoreServices core, Album album, byte[] bytes, string name)
    {
        var temp = Path.Combine(Path.GetTempPath(), "monalbumpc-tests", Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(temp, bytes);
        return core.Import.Import(new IncomingFile(temp, Hashing.Sha256(bytes), name, album.Id, null, ImportChannel.Wifi));
    }
}
