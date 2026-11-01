using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Storage;
using MonAlbumPc.Core.Tests.Pairing;

namespace MonAlbumPc.Core.Tests.Import;

public sealed class SessionTrackerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly SessionStore _store;
    private readonly SessionTracker _tracker;
    private readonly Guid _device = Guid.NewGuid();
    private readonly Guid _album = Guid.NewGuid();

    public SessionTrackerTests()
    {
        _store = new SessionStore(new Database(_temp.Combine("catalog.db")));
        _tracker = new SessionTracker(_store, _clock);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Files_received_in_a_row_make_one_session_closed_after_idle_time()
    {
        var ended = new List<BackupSession>();
        _tracker.Ended += ended.Add;

        _tracker.RecordSaved(_device, _album, ImportChannel.Wifi, isVideo: false);
        _clock.Advance(TimeSpan.FromMinutes(1));
        _tracker.RecordSaved(_device, _album, ImportChannel.Wifi, isVideo: true);
        _clock.Advance(TimeSpan.FromMinutes(1));
        _tracker.CloseIdle();
        Assert.Single(_tracker.Active);
        Assert.Empty(ended);

        _clock.Advance(SessionTracker.IdleTimeout);
        _tracker.CloseIdle();

        Assert.Empty(_tracker.Active);
        var session = Assert.Single(ended);
        Assert.Equal((1, 1), (session.Photos, session.Videos));
        Assert.Equal(TimeSpan.FromMinutes(1), session.EndedAt - session.StartedAt);
        Assert.Equal(session, Assert.Single(_store.List()));
    }

    [Fact]
    public void Errors_end_the_session_immediately()
    {
        BackupSession? ended = null;
        _tracker.Ended += s => ended = s;
        _tracker.RecordSaved(_device, _album, ImportChannel.Wifi, isVideo: false);

        _tracker.RecordError(_device, _album, ImportChannel.Wifi, "disk_full", "Le disque est plein.");

        Assert.NotNull(ended);
        Assert.Equal("disk_full", ended.ErrorCode);
        Assert.Equal(1, ended.Photos);
        Assert.Empty(_tracker.Active);
    }

    [Fact]
    public void Connections_that_bring_nothing_are_not_recorded()
    {
        var ended = false;
        _tracker.Ended += _ => ended = true;
        _tracker.Touch(_device, _album, ImportChannel.Wifi);

        _clock.Advance(SessionTracker.IdleTimeout);
        _tracker.CloseIdle();

        Assert.False(ended);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void A_phone_with_nothing_left_closes_its_session_at_once_as_complete()
    {
        var ended = new List<BackupSession>();
        _tracker.Ended += ended.Add;
        _tracker.RecordSaved(_device, _album, ImportChannel.Wifi, isVideo: false);

        _tracker.RecordFinished(_device, _album, ImportChannel.Wifi, complete: true);

        Assert.Empty(_tracker.Active);
        var session = Assert.Single(ended);
        Assert.True(session.Complete);
        Assert.Equal(1, session.Photos);
        Assert.True(Assert.Single(_store.List()).Complete);
    }

    [Fact]
    public void A_complete_phone_without_open_session_is_still_recorded_so_it_can_be_announced()
    {
        var ended = new List<BackupSession>();
        _tracker.Ended += ended.Add;

        _tracker.RecordFinished(_device, _album, ImportChannel.Wifi, complete: true);

        var session = Assert.Single(ended);
        Assert.True(session.Complete);
        Assert.Equal((0, 0), (session.Photos, session.Videos));
        Assert.Equal(_device, session.DeviceId);
        Assert.Empty(_tracker.Active);
        Assert.Single(_store.List());
    }

    [Fact]
    public void A_phone_with_work_left_closes_its_session_without_marking_it_complete()
    {
        var ended = new List<BackupSession>();
        _tracker.Ended += ended.Add;
        _tracker.RecordSaved(_device, _album, ImportChannel.Wifi, isVideo: true);

        _tracker.RecordFinished(_device, _album, ImportChannel.Wifi, complete: false);

        var session = Assert.Single(ended);
        Assert.False(session.Complete);
        Assert.Equal(1, session.Videos);
        Assert.Empty(_tracker.Active);
    }

    [Fact]
    public void A_session_with_nothing_and_not_complete_stays_unrecorded()
    {
        var ended = false;
        _tracker.Ended += _ => ended = true;

        _tracker.RecordFinished(_device, _album, ImportChannel.Wifi, complete: false);
        Assert.False(ended);

        _tracker.Touch(_device, _album, ImportChannel.Wifi);
        _tracker.RecordFinished(_device, _album, ImportChannel.Wifi, complete: false);

        Assert.False(ended);
        Assert.Empty(_tracker.Active);
        Assert.Empty(_store.List());
    }

    [Fact]
    public void A_session_closed_by_silence_is_not_complete()
    {
        _tracker.RecordSaved(_device, _album, ImportChannel.Wifi, isVideo: false);
        _clock.Advance(SessionTracker.IdleTimeout);

        _tracker.CloseIdle();

        Assert.False(Assert.Single(_store.List()).Complete);
    }
}
