using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;
using MonAlbumPc.Core.Tags;
using static MonAlbumPc.Core.Tests.Media.XmpKeywordsTests;

namespace MonAlbumPc.Core.Tests.Tags;

/// <summary>A NAS and a Windows viewer sharing an album folder, each with its own catalog.</summary>
public sealed class TagServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CoreServices _nas;
    private readonly CoreServices _viewer;
    private readonly Album _album;

    public TagServiceTests()
    {
        _nas = CoreServices.Create(_temp.Combine("nas"));
        _viewer = CoreServices.Create(_temp.Combine("viewer"));
        _album = new Album(Guid.NewGuid(), "Nicolas", _temp.Combine("library", "Nico"), "#0F6CBD", DateTimeOffset.Now);
        _nas.Albums.Add(_album);
        _viewer.Albums.Add(_album);
        Directory.CreateDirectory(_album.DestinationRoot);
    }

    public void Dispose()
    {
        _nas.Dispose();
        _viewer.Dispose();
        _temp.Dispose();
    }

    private string Photo(string name, byte[]? content = null)
    {
        var path = Path.Combine(_album.DestinationRoot, "2024", "2024-05", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? PhoneJpeg());
        return path;
    }

    private static IReadOnlyList<string> TagsOf(CoreServices core, string path) =>
        core.Tags.ByFile().Where(f => path.Replace('\\', '/').EndsWith(f.Key.Path, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Value).SingleOrDefault() ?? [];

    [Fact]
    public void The_index_reads_tags_of_photos_and_video_sidecars()
    {
        var photo = Photo("IMG_1.jpg");
        var video = Photo("VID_1.mp4", [1, 2, 3]);
        UserTags.Change(photo, ["Bricolage/ESP32", "boulot"], []);
        UserTags.Change(video, ["Travaux maison"], []);

        _nas.Media.Refresh(_album);

        Assert.Equal(["Bricolage/ESP32", "boulot"], TagsOf(_nas, photo).Order(StringComparer.Ordinal));
        Assert.Equal(["Travaux maison"], TagsOf(_nas, video));
        Assert.Equal(["boulot", "Bricolage/ESP32", "Travaux maison"], _nas.Tags.List().Select(t => t.Name));
    }

    [Fact]
    public void Provenance_keywords_are_not_listed_as_tags()
    {
        var source = _temp.WriteFile("upload.tmp", PhoneJpeg());
        var saved = _nas.Import.Import(new IncomingFile(source, Hashing.Sha256File(source), "IMG-20240512-WA0007.jpg", _album.Id,
            null, ImportChannel.Wifi, "WhatsApp Images/IMG-20240512-WA0007.jpg")).DestinationPath!;
        var telegram = Photo("photo.jpg");
        XmpKeywords.Add(telegram, "Telegram"); // provenance guessed from nothing: a keyword of another tool
        UserTags.Change(saved, ["boulot"], [], keep: "WhatsApp");

        _nas.Media.Refresh(_album);

        Assert.Equal(["boulot"], TagsOf(_nas, saved));
        Assert.Equal(["WhatsApp", "boulot"], XmpKeywords.Read(saved));
        Assert.DoesNotContain(_nas.Tags.List(), t => t.Name == "WhatsApp");
        Assert.Equal(["Telegram"], TagsOf(_nas, telegram));
    }

    [Fact]
    public async Task Tags_written_by_the_nas_reach_the_viewer_index_even_when_the_size_does_not_change()
    {
        var photo = Photo("IMG_1.jpg");
        _nas.Media.Refresh(_album);
        _viewer.Media.Refresh(_album);
        var target = new TagTarget(_album, photo);

        await _nas.Tags.AddAsync([target], "abc");
        Assert.Equal(["abc"], TagsOf(_nas, photo)); // written into the index at once
        Assert.Equal(1, _viewer.Tags.Poll());
        Assert.Equal(["abc"], TagsOf(_viewer, photo));

        var size = new FileInfo(photo).Length;
        var modified = File.GetLastWriteTimeUtc(photo);
        await _nas.Tags.RenameAsync("abc", "xyz");
        Assert.Equal(size, new FileInfo(photo).Length);
        Assert.Equal(modified, File.GetLastWriteTimeUtc(photo));

        var refresh = _viewer.Media.Refresh(_album); // same size, same date: only the journal tells

        Assert.Equal(0, refresh.Read);
        Assert.Equal(1, refresh.Tagged);
        Assert.Equal(["xyz"], TagsOf(_viewer, photo));
    }

    [Fact]
    public async Task The_viewer_tags_over_the_share_and_the_nas_takes_it_from_the_journal()
    {
        var video = Photo("VID_1.mp4", [1, 2, 3, 4]);
        _nas.Media.Refresh(_album);

        await _viewer.Tags.AddAsync([new TagTarget(_album, video)], "Travaux maison");

        Assert.True(File.Exists(video + ".xmp"));
        Assert.True(File.Exists(Path.Combine(TagJournals.Folder(_album.DestinationRoot), _viewer.Identity.PcId + ".jsonl")));
        Assert.Equal(1, _nas.Tags.Poll());
        Assert.Equal(["Travaux maison"], TagsOf(_nas, video));
    }

    [Fact]
    public void A_sidecar_changed_by_another_program_is_read_again()
    {
        var video = Photo("VID_1.mp4", [1, 2, 3, 4]);
        _nas.Media.Refresh(_album);
        Assert.Empty(TagsOf(_nas, video));

        UserTags.Change(video, ["digiKam"], []); // as digiKam would, without telling anyone
        var result = _nas.Media.Refresh(_album);

        Assert.Equal(1, result.Read);
        Assert.Equal(["digiKam"], TagsOf(_nas, video));
    }

    [Fact]
    public async Task A_tag_created_without_photos_is_kept_and_shared()
    {
        Assert.Equal("Vacances/Bretagne", await _nas.Tags.CreateAsync(" Vacances / Bretagne "));

        Assert.Equal([new TagCount("Vacances/Bretagne", 0)], _nas.Tags.List());
        _viewer.Tags.Poll();
        Assert.Equal([new TagCount("Vacances/Bretagne", 0)], _viewer.Tags.List());

        await _viewer.Tags.DeleteAsync("Vacances/Bretagne");
        Assert.Empty(_viewer.Tags.List());
        _nas.Tags.Poll();
        Assert.Empty(_nas.Tags.List());
    }

    [Fact]
    public async Task Renaming_a_parent_renames_its_children_and_merging_joins_an_existing_tag()
    {
        var one = Photo("IMG_1.jpg", PhoneJpeg(1));
        var two = Photo("IMG_2.jpg", PhoneJpeg(2));
        _nas.Media.Refresh(_album);
        await _nas.Tags.AddAsync([new TagTarget(_album, one)], "Bricolage/ESP32");
        await _nas.Tags.AddAsync([new TagTarget(_album, one), new TagTarget(_album, two)], "Bricolage");
        await _nas.Tags.AddAsync([new TagTarget(_album, two)], "Travail");
        Assert.Equal(2, _nas.Tags.CountWithin("Bricolage"));
        Assert.Equal(["Bricolage/ESP32"], _nas.Tags.Children("Bricolage"));

        var renamed = await _nas.Tags.RenameAsync("Bricolage", "DIY");
        Assert.Equal(2, renamed.Changed);
        Assert.Equal(["DIY", "DIY/ESP32"], UserTags.Read(one).Order());

        var merged = await _nas.Tags.RenameAsync("DIY", "travail");
        Assert.StartsWith("Tag « DIY » fusionné dans « Travail » sur 2 photos", merged.Summary);
        Assert.Equal(["Travail", "Travail/ESP32"], UserTags.Read(one).Order());
        Assert.Equal(["Travail"], UserTags.Read(two));
        Assert.Equal(["Travail", "Travail/ESP32"], _nas.Tags.List().Select(t => t.Name));

        var deleted = await _nas.Tags.DeleteAsync("Travail");
        Assert.Equal(2, deleted.Changed);
        Assert.Empty(UserTags.Read(one));
        Assert.Empty(_nas.Tags.List());
    }

    [Fact]
    public async Task Tagging_tells_its_progress_and_its_failures()
    {
        var photo = Photo("IMG_1.jpg");
        var gone = Path.Combine(_album.DestinationRoot, "absent.jpg");

        var result = await _nas.Tags.AddAsync([new TagTarget(_album, photo), new TagTarget(_album, gone)], "boulot");

        Assert.Equal(1, result.Changed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("absent.jpg", failure.Name);
        Assert.StartsWith("Tag « boulot » ajouté à 1 photo, 1 en échec en ", result.Summary);
        Assert.Contains(_nas.Work.Events, e => e.Text == result.Summary);
        Assert.Empty(_nas.Work.Running);
    }

    [Fact]
    public async Task A_tagged_photo_is_still_recognized_and_not_imported_again()
    {
        var bytes = PhoneJpeg(3);
        var source = _temp.WriteFile("upload.tmp", bytes);
        var sha = Hashing.Sha256File(source);
        var saved = _nas.Import.Import(new IncomingFile(source, sha, "IMG-20240512-WA0001.jpg", _album.Id, null, ImportChannel.Wifi,
            "WhatsApp Images/IMG-20240512-WA0001.jpg")).DestinationPath!;
        _nas.Media.Refresh(_album);

        await _nas.Tags.AddAsync([new TagTarget(_album, saved)], "Bricolage/ESP32");
        Assert.NotEqual(bytes.Length, new FileInfo(saved).Length);

        Assert.True(_nas.History.ContainsNameAndSize(_album.Id, "IMG-20240512-WA0001.jpg", bytes.Length));
        Assert.True(_viewer.History.ContainsNameAndSize(_album.Id, "IMG-20240512-WA0001.jpg", bytes.Length)); // from the journal
        Assert.True(_viewer.History.Contains(_album.Id, sha));
        var again = _temp.WriteFile("again.tmp", bytes);
        Assert.Equal(ImportOutcome.AlreadyPresent,
            _nas.Import.Import(new IncomingFile(again, sha, "IMG-20240512-WA0001.jpg", _album.Id, null, ImportChannel.Wifi)).Outcome);
        Assert.Single(Directory.GetFiles(_album.DestinationRoot, "*.jpg", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_file_not_indexed_yet_shows_its_tags_at_once_and_forgets_them_once_deleted()
    {
        var photo = Photo("IMG_1.jpg");

        await _nas.Tags.AddAsync([new TagTarget(_album, photo)], "boulot");
        Assert.Equal(["boulot"], TagsOf(_nas, photo));

        File.Delete(photo);
        _nas.Media.Refresh(_album);
        Assert.Empty(_nas.Tags.ByFile());
    }

    [Fact]
    public void Files_indexed_before_tags_existed_are_read_once_for_their_tags()
    {
        var photo = Photo("IMG_1.jpg");
        UserTags.Change(photo, ["ancien"], []);
        _nas.Media.Refresh(_album);
        using (var connection = _nas.Database.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE media SET tags_read = 0; DELETE FROM media_tags;";
            command.ExecuteNonQuery();
        }

        Assert.Equal(1, _nas.Media.Refresh(_album).Read);
        Assert.Equal(["ancien"], TagsOf(_nas, photo));
        Assert.Equal(0, _nas.Media.Refresh(_album).Read);
    }
}
