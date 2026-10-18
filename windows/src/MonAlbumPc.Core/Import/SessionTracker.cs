using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Import;

/// <summary>A backup in progress, for the "Réception en cours" status.</summary>
public sealed record ActiveSession(Guid DeviceId, Guid AlbumId, ImportChannel Channel, DateTimeOffset StartedAt, int Photos, int Videos);

/// <summary>
/// Groups what a phone sends into sessions: a session stays open while files keep arriving and is closed (saved to
/// the history, announced) once the phone has been quiet for <see cref="IdleTimeout"/> or an error stops it.
/// </summary>
public sealed class SessionTracker(SessionStore store, TimeProvider clock)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Open> _open = [];

    /// <summary>Raised when an active session starts, progresses or ends.</summary>
    public event Action? Changed;

    /// <summary>Raised with each session written to the history.</summary>
    public event Action<BackupSession>? Ended;

    public IReadOnlyList<ActiveSession> Active
    {
        get
        {
            lock (_lock)
            {
                return [.. _open.Values.Select(o => new ActiveSession(o.DeviceId, o.AlbumId, o.Channel, o.StartedAt, o.Photos, o.Videos))];
            }
        }
    }

    /// <summary>The phone is sending something (an upload started or resumed).</summary>
    public void Touch(Guid deviceId, Guid albumId, ImportChannel channel)
    {
        lock (_lock)
        {
            OpenFor(deviceId, albumId, channel);
        }
        Changed?.Invoke();
    }

    public void RecordSaved(Guid deviceId, Guid albumId, ImportChannel channel, bool isVideo)
    {
        lock (_lock)
        {
            var session = OpenFor(deviceId, albumId, channel);
            if (isVideo)
            {
                session.Videos++;
            }
            else
            {
                session.Photos++;
            }
        }
        Changed?.Invoke();
    }

    /// <summary>An error stopped the phone (destination unavailable, disk full…): the session ends with it.</summary>
    public void RecordError(Guid deviceId, Guid albumId, ImportChannel channel, string code, string message)
    {
        BackupSession ended;
        lock (_lock)
        {
            var session = OpenFor(deviceId, albumId, channel);
            session.ErrorCode = code;
            session.ErrorMessage = message;
            ended = Close(session);
        }
        Publish(ended);
    }

    /// <summary>Closes the sessions that have been idle long enough. Called periodically.</summary>
    public void CloseIdle()
    {
        List<BackupSession> ended;
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            ended = [.. _open.Values.Where(o => now - o.LastActivity >= IdleTimeout).ToList().Select(Close)];
        }
        foreach (var session in ended)
        {
            Publish(session);
        }
    }

    private Open OpenFor(Guid deviceId, Guid albumId, ImportChannel channel)
    {
        var now = clock.GetLocalNow();
        if (!_open.TryGetValue(deviceId, out var session))
        {
            session = new Open(deviceId, albumId, channel, now);
            _open[deviceId] = session;
        }
        session.LastActivity = now;
        return session;
    }

    private BackupSession Close(Open session)
    {
        _open.Remove(session.DeviceId);
        var record = new BackupSession(0, session.DeviceId, session.AlbumId, session.Channel, session.StartedAt,
            session.LastActivity, session.Photos, session.Videos, session.ErrorCode, session.ErrorMessage);
        // A connection that brought nothing and failed nothing is not worth a line in the history.
        return record.Photos + record.Videos == 0 && !record.HasError ? record : record with { Id = store.Add(record) };
    }

    private void Publish(BackupSession session)
    {
        Changed?.Invoke();
        if (session.Id != 0)
        {
            Ended?.Invoke(session);
        }
    }

    private sealed class Open(Guid deviceId, Guid albumId, ImportChannel channel, DateTimeOffset startedAt)
    {
        public Guid DeviceId { get; } = deviceId;
        public Guid AlbumId { get; } = albumId;
        public ImportChannel Channel { get; } = channel;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public DateTimeOffset LastActivity { get; set; } = startedAt;
        public int Photos { get; set; }
        public int Videos { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
