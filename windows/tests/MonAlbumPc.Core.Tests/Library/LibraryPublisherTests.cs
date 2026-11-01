using MonAlbumPc.Core.Library;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Library;

public sealed class LibraryPublisherTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CoreServices _core;
    private readonly string _library;

    public LibraryPublisherTests()
    {
        _core = CoreServices.Create(_temp.Combine("config"));
        _core.PcName = "NAS";
        _library = _temp.Combine("photos");
        Directory.CreateDirectory(_library);
    }

    public void Dispose()
    {
        _core.Dispose();
        _temp.Dispose();
    }

    private Album NewAlbum(string name) =>
        new(Guid.NewGuid(), name, Path.Combine(_library, name), "#0F6CBD", DateTimeOffset.Now);

    [Fact]
    public void The_manifest_is_written_at_start_and_follows_the_albums()
    {
        var first = NewAlbum("Nico");
        _core.Albums.Add(first);
        var publisher = new LibraryPublisher(_core, _library);

        publisher.Start();

        var manifest = LibraryManifest.Read(_library)!;
        Assert.Equal("NAS", manifest.ServerName);
        Assert.Equal(["Nico"], manifest.Albums.Select(a => a.Folder));

        _core.Albums.Add(NewAlbum("Marie"));
        Assert.Equal(2, LibraryManifest.Read(_library)!.Albums.Count);

        _core.Albums.Update(first with { Name = "Nicolas" });
        Assert.Contains(LibraryManifest.Read(_library)!.Albums, a => a.Name == "Nicolas");

        _core.Albums.Delete(first.Id);
        Assert.Equal(["Marie"], LibraryManifest.Read(_library)!.Albums.Select(a => a.Name));
    }

    [Fact]
    public void Each_finished_session_lands_in_the_activity_feed()
    {
        var album = NewAlbum("Nico");
        _core.Albums.Add(album);
        new LibraryPublisher(_core, _library).Start();
        var phone = Guid.NewGuid();

        _core.Sessions.RecordSaved(phone, album.Id, ImportChannel.Wifi, isVideo: false);
        _core.Sessions.RecordFinished(phone, album.Id, ImportChannel.Wifi, complete: true);

        var entry = Assert.Single(ActivityFeed.ReadNew(_library, new Dictionary<string, long>()));
        Assert.Equal("Nico", entry.AlbumName);
        Assert.Equal(1, entry.Photos);
        Assert.True(entry.Complete);
        Assert.True(File.Exists(ActivityFeed.FilePath(_library, _core.Identity.PcId)));
    }

    [Fact]
    public void A_library_out_of_reach_never_stops_a_backup()
    {
        var blocked = _temp.WriteFile("blocked", [1]); // a file where a folder is expected
        var publisher = new LibraryPublisher(_core, blocked);
        var failures = new List<string>();
        publisher.Failed += (what, _) => failures.Add(what);
        var album = NewAlbum("Nico");
        _core.Albums.Add(album);

        publisher.Start();
        _core.Albums.Update(album with { Name = "Nicolas" });
        _core.Sessions.RecordError(Guid.NewGuid(), album.Id, ImportChannel.Wifi, "disk_full", "Plein");

        Assert.Equal(["library.json", "library.json", "activity"], failures);
    }
}
