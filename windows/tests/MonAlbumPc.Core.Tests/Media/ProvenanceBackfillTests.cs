using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Media;

public sealed class ProvenanceBackfillTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CoreServices _core;
    private readonly Album _album;

    public ProvenanceBackfillTests()
    {
        _core = CoreServices.Create(_temp.Combine("config"));
        _album = new Album(Guid.NewGuid(), "Nicolas", _temp.Combine("photos", "Nicolas"), "#000", DateTimeOffset.Now);
        _core.Albums.Add(_album);
        Directory.CreateDirectory(_album.DestinationRoot);
    }

    public void Dispose()
    {
        _core.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public void Marks_what_is_not_from_the_camera_once_and_keeps_the_rest_as_it_is()
    {
        // Copied by hand long ago: only the name tells.
        var whatsApp = Put("2023/2023-11/IMG-20231126-WA0007.jpg", TestImages.Jpeg(seed: 1));
        var camera = Put("2023/2023-11/IMG_20231126_101010.jpg", TestImages.Jpeg(seed: 2));
        var video = Put("2023/2023-11/VID-20231126-WA0001.mp4", "video"u8.ToArray());
        var png = Put("2023/2023-11/Screenshot_20231126-101010.png", TestImages.Jpeg(seed: 3, format: ImageMagick.MagickFormat.Png));
        // Journaled by another server, with its folder.
        var telegram = Put("2023/2023-11/photo.jpg", TestImages.Jpeg(seed: 4));
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(AlbumJournals.Folder(_album.DestinationRoot)).FullName, $"{Guid.NewGuid()}.jsonl"),
            """{"sha256":"abc","name":"photo.jpg","size":10,"path":"2023/2023-11/photo.jpg","takenAt":null,"device":null,"importedAt":"2026-10-20T08:00:00+02:00","source":"Telegram"}""" + "\n");
        var modified = new DateTime(2023, 11, 26, 10, 12, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(whatsApp, modified);
        var cameraBytes = File.ReadAllBytes(camera);
        var steps = new List<ProvenanceProgress>();

        var plan = ProvenanceBackfill.Plan(_core.History, [_album], new Recorder(steps));

        Assert.Equal(1, plan.Count(MediaSourceKind.WhatsApp));
        Assert.Equal(1, plan.Count(MediaSourceKind.Other));
        Assert.Equal(1, plan.OtherFormats);
        Assert.Equal(
            "1 photo WhatsApp et 1 photo d'une autre application seront marquées ; les vidéos et les photos de l'appareil photo ne sont pas modifiées."
            + " 1 image dans un autre format que JPEG (PNG…) reste telle quelle : les marquer demanderait de les réécrire.",
            plan.Description.Replace(' ', ' '));
        Assert.Contains(steps, s => s.Step.StartsWith("Lecture du catalogue", StringComparison.Ordinal));

        var summary = ProvenanceBackfill.Run(plan, new Recorder(steps));

        Assert.Equal(2, summary.Tagged);
        Assert.Empty(summary.Problems);
        Assert.Equal(["WhatsApp"], XmpKeywords.Read(whatsApp));
        Assert.Equal(["Telegram"], XmpKeywords.Read(telegram));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(whatsApp));
        Assert.Equal(cameraBytes, File.ReadAllBytes(camera));
        Assert.Equal("video"u8.ToArray(), File.ReadAllBytes(video));
        Assert.Contains(steps, s => s is { Step: "Marquage", File: "photo.jpg" });

        var again = ProvenanceBackfill.Plan(_core.History, [_album]);
        Assert.Empty(again.Files);
        Assert.Equal(2, again.AlreadyTagged);
    }

    [Fact]
    public void Photos_marked_afterwards_are_still_recognized_by_the_phone()
    {
        // Imported before the keywords existed: stored as sent.
        const string name = "IMG-20231126-WA0007.jpg";
        var sent = TestImages.Jpeg("2023:11:26 10:12:00");
        var saved = Put($"2023/2023-11/{name}", sent);
        _core.History.Record(new ImportRecord(Hashing.Sha256(sent), _album.Id, null, sent.Length, name, $"WhatsApp Images/{name}",
            saved, new DateTime(2023, 11, 26, 10, 12, 0), ImportChannel.Wifi, DateTimeOffset.Now));

        ProvenanceBackfill.Run(ProvenanceBackfill.Plan(_core.History, [_album]));

        Assert.Equal(["WhatsApp"], XmpKeywords.Read(saved));
        Assert.True(_core.History.Contains(_album.Id, Hashing.Sha256(sent)));
        Assert.True(_core.History.ContainsNameAndSize(_album.Id, name, sent.Length));
        var temp = _temp.WriteFile(Path.Combine("uploads", "1"), sent);
        Assert.Equal(ImportOutcome.AlreadyPresent,
            _core.Import.Import(new IncomingFile(temp, Hashing.Sha256(sent), name, _album.Id, null, ImportChannel.Wifi)).Outcome);
    }

    private string Put(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(_album.DestinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class Recorder(List<ProvenanceProgress> steps) : IProgress<ProvenanceProgress>
    {
        public void Report(ProvenanceProgress value) => steps.Add(value);
    }
}
