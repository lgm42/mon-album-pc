using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Storage;

public sealed class StoresTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(2));
    private readonly TempDirectory _temp = new();
    private readonly Database _database;
    private readonly AlbumStore _albums;
    private readonly DeviceStore _devices;
    private readonly ImportHistory _history;

    public StoresTests()
    {
        _database = new Database(_temp.Combine("catalog.db"));
        _albums = new AlbumStore(_database);
        _devices = new DeviceStore(_database);
        _history = new ImportHistory(_database);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Albums_round_trip()
    {
        var album = NewAlbum("Marie");
        _albums.Add(album);
        _albums.Update(album with { Name = "Marie D." });

        var stored = Assert.Single(_albums.List());
        Assert.Equal("Marie D.", stored.Name);
        Assert.Equal(album.DestinationRoot, stored.DestinationRoot);
        Assert.Equal(album.CreatedAt, stored.CreatedAt);
    }

    [Fact]
    public void Migrations_are_idempotent()
    {
        _albums.Add(NewAlbum("Marie"));
        var reopened = new AlbumStore(new Database(_temp.Combine("catalog.db")));
        Assert.Single(reopened.List());
    }

    [Fact]
    public void Album_with_active_device_cannot_be_deleted()
    {
        var album = NewAlbum("Marie");
        _albums.Add(album);
        var device = NewDevice(album.Id);
        _devices.Add(device);

        Assert.Throws<InvalidOperationException>(() => _albums.Delete(album.Id));

        _devices.Revoke(device.Id, Now);
        _albums.Delete(album.Id);
        Assert.Empty(_albums.List());
    }

    [Fact]
    public void Revoked_devices_leave_the_album_but_keep_their_history()
    {
        var album = NewAlbum("Marie");
        _albums.Add(album);
        var device = NewDevice(album.Id);
        _devices.Add(device);
        _history.Record(NewImport(album.Id, device.Id, "aa"));

        _devices.Revoke(device.Id, Now);

        Assert.Empty(_devices.ListByAlbum(album.Id));
        Assert.False(_devices.Get(device.Id)!.IsActive);
        Assert.True(_history.Contains(album.Id, "aa"));
        Assert.Equal(1, _history.CountByDevice(device.Id));
    }

    [Fact]
    public void Device_touch_updates_last_seen_and_version()
    {
        var album = NewAlbum("Marie");
        _albums.Add(album);
        var device = NewDevice(album.Id);
        _devices.Add(device);

        _devices.Touch(device.Id, Now.AddHours(1), "1.2.0");
        _devices.Touch(device.Id, Now.AddHours(2), null);

        var stored = _devices.Get(device.Id)!;
        Assert.Equal(Now.AddHours(2), stored.LastSeenAt);
        Assert.Equal("1.2.0", stored.AppVersion);
        Assert.Equal(device.SecretHash, stored.SecretHash);
    }

    [Fact]
    public void Merging_a_duplicate_moves_its_history_to_the_kept_phone()
    {
        var album = NewAlbum("Marie");
        _albums.Add(album);
        var old = NewDevice(album.Id) with { HardwareId = "old-id" };
        var kept = NewDevice(album.Id);
        _devices.Add(old);
        _devices.Add(kept);
        _history.Record(NewImport(album.Id, old.Id, "aa"));
        var sessions = new SessionStore(_database);
        sessions.Add(new BackupSession(0, old.Id, album.Id, ImportChannel.Wifi, Now, Now, 1, 0, null, null));

        _devices.Merge(old.Id, kept.Id);

        Assert.Equal(kept.Id, Assert.Single(_devices.ListByAlbum(album.Id)).Id);
        Assert.Null(_devices.Get(old.Id));
        Assert.Equal(1, _history.CountByDevice(kept.Id));
        Assert.Equal(kept.Id, Assert.Single(sessions.List()).DeviceId);
        Assert.Equal("old-id", _devices.Get(kept.Id)!.HardwareId);
    }

    [Fact]
    public void Phones_bearing_the_same_name_are_merged_into_the_active_one()
    {
        var album = NewAlbum("Marie");
        _albums.Add(album);
        var old = NewDevice(album.Id) with { Name = "POCO X7 Pro", PairedAt = Now.AddDays(-2) };
        var current = NewDevice(album.Id) with { Name = "POCO X7 Pro", PairedAt = Now };
        var revoked = NewDevice(album.Id) with { Name = "poco x7 pro", PairedAt = Now.AddDays(1), RevokedAt = Now.AddDays(1) };
        var other = NewDevice(album.Id) with { Name = "A35 de Paul" };
        foreach (var device in new[] { old, current, revoked, other })
        {
            _devices.Add(device);
        }
        _history.Record(NewImport(album.Id, old.Id, "aa"));

        _devices.MergeSameNames();

        Assert.Equal(new[] { current.Id, other.Id }.Order(), _devices.ListByAlbum(album.Id).Select(d => d.Id).Order());
        Assert.Null(_devices.Get(old.Id));
        Assert.Null(_devices.Get(revoked.Id));
        Assert.Equal(1, _history.CountByDevice(current.Id));
    }

    [Fact]
    public void History_matches_by_hash_within_the_album_only()
    {
        var marie = NewAlbum("Marie");
        var paul = NewAlbum("Paul");
        _albums.Add(marie);
        _albums.Add(paul);
        _history.Record(NewImport(marie.Id, null, "aa"));

        Assert.True(_history.Contains(marie.Id, "aa"));
        Assert.False(_history.Contains(paul.Id, "aa"));
        Assert.Equal(["aa"], _history.ContainsMany(marie.Id, ["aa", "bb"]));
    }

    [Fact]
    public void Index_supports_hash_and_loose_name_date_match()
    {
        var album = NewAlbum("Marie");
        _albums.Add(album);
        var taken = new DateTime(2024, 5, 12, 10, 30, 15);
        _history.UpsertIndexEntry(new IndexEntry(album.Id, @"2024\IMG_1.jpg", "cc", 10, "IMG_1.jpg", taken, Now));

        Assert.True(_history.Contains(album.Id, "cc"));
        Assert.True(_history.ContainsByNameAndDate(album.Id, "img_1.JPG", taken));
        Assert.False(_history.ContainsByNameAndDate(album.Id, "IMG_1.jpg", taken.AddSeconds(1)));

        _history.RemoveIndexEntry(album.Id, @"2024\IMG_1.jpg");
        Assert.False(_history.Contains(album.Id, "cc"));
    }

    [Fact]
    public void Usb_fallback_matches_device_path_and_size()
    {
        var album = NewAlbum("Marie");
        _albums.Add(album);
        var device = Guid.NewGuid();
        _history.Record(NewImport(album.Id, device, "dd") with { SourcePath = "DCIM/Camera/a.jpg", Size = 42 });

        Assert.True(_history.ContainsSource(device, "DCIM/Camera/a.jpg", 42));
        Assert.False(_history.ContainsSource(device, "DCIM/Camera/a.jpg", 43));
        Assert.False(_history.ContainsSource(Guid.NewGuid(), "DCIM/Camera/a.jpg", 42));
    }

    private Album NewAlbum(string name) =>
        new(Guid.NewGuid(), name, _temp.Combine("Photos", name), _albums.NextColor(), Now);

    private static Device NewDevice(Guid albumId) =>
        new(Guid.NewGuid(), albumId, "A35 de Marie", "Samsung Galaxy A35", [1, 2, 3], "1.0.0", Now, null, null);

    private static ImportRecord NewImport(Guid albumId, Guid? deviceId, string sha) =>
        new(sha, albumId, deviceId, 100, "IMG.jpg", null, @"D:\Photos\IMG.jpg", null, ImportChannel.Wifi, Now);
}
