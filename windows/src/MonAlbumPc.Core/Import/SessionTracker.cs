using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Import;

/// <summary>The file a phone is sending right now; <paramref name="Since"/> and <paramref name="ReceivedSince"/> give its pace.</summary>
public sealed record IncomingTransfer(string Name, long Size, long Received, DateTimeOffset Since = default, long ReceivedSince = 0);

/// <summary>A file the server could not store; the phone goes on with the others.</summary>
public sealed record FileFailure(string Name, string Message);

/// <summary>A backup in progress, for the "Réception en cours" status.</summary>
/// <param name="Comparing">The phone is asking which of its files are already here (check, check-names).</param>
/// <param name="Position">Rank of <paramref name="Current"/> in the phone's run, when the phone tells it (1 = first).</param>
/// <param name="Total">Number of files of the phone's run, when the phone tells it.</param>
public sealed record ActiveSession(
    Guid DeviceId,
    Guid AlbumId,
    ImportChannel Channel,
    DateTimeOffset StartedAt,
    int Photos,
    int Videos,
    DateTimeOffset LastActivity = default,
    bool Comparing = false,
    IncomingTransfer? Current = null,
    int? Position = null,
    int? Total = null,
    int Failures = 0,
    FileFailure? LastFailure = null)
{
    public int Received => Photos + Videos;

    /// <summary>Nothing came from the phone for a while: it may have lost the network or been put to sleep.</summary>
    public bool IsWaiting(DateTimeOffset now) => now - LastActivity >= SessionTracker.WaitingAfter;

    /// <summary>Pace of the current file since it started or resumed, once there is enough to measure.</summary>
    public double? BytesPerSecond => Current is { } file && LastActivity - file.Since is { TotalSeconds: >= 2 } elapsed
        && file.Received > file.ReceivedSince
        ? (file.Received - file.ReceivedSince) / elapsed.TotalSeconds
        : null;
}

/// <summary>The last backup written to the history. <see cref="Errors"/> is unknown for a session read back after a restart.</summary>
public sealed record FinishedSession(BackupSession Session, int? Errors);

/// <summary>Error codes of the sessions besides those of protocol §6.</summary>
public static class SessionErrors
{
    /// <summary>Files arrived but could not be stored; the phone went on with the others.</summary>
    public const string ImportFailed = "import_failed";
}

/// <summary>
/// Groups what a phone sends into sessions: a session stays open while files keep arriving and is closed (saved to
/// the history, announced) once the phone has been quiet for <see cref="IdleTimeout"/> or an error stops it.
/// </summary>
public sealed class SessionTracker(SessionStore store, TimeProvider clock)
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Silence after which a session is shown as waiting for the phone rather than receiving.</summary>
    public static readonly TimeSpan WaitingAfter = TimeSpan.FromSeconds(30);

    // Bytes arrive in 64 KB blocks: the status is redrawn at most this often.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Open> _open = [];
    private FinishedSession? _last;
    private bool _lastLoaded;

    /// <summary>Raised when an active session starts, progresses or ends.</summary>
    public event Action? Changed;

    /// <summary>Raised when a phone starts a session.</summary>
    public event Action<ActiveSession>? Started;

    /// <summary>Raised with each closed session, including those not worth a line in the history (<c>Id == 0</c>).</summary>
    public event Action<BackupSession>? Closed;

    /// <summary>Raised with each session written to the history.</summary>
    public event Action<BackupSession>? Ended;

    public IReadOnlyList<ActiveSession> Active
    {
        get
        {
            lock (_lock)
            {
                return [.. _open.Values.Select(o => o.Snapshot())];
            }
        }
    }

    /// <summary>The last session written to the history, or null if there is none.</summary>
    public FinishedSession? Last
    {
        get
        {
            lock (_lock)
            {
                if (!_lastLoaded)
                {
                    _last ??= store.List(1) is [var session] ? new FinishedSession(session, null) : null;
                    _lastLoaded = true;
                }
                return _last;
            }
        }
    }

    /// <summary>The phone is sending something (an upload started or resumed).</summary>
    public void Touch(Guid deviceId, Guid albumId, ImportChannel channel) => Update(deviceId, albumId, channel, _ => { });

    /// <summary>The phone asks which of its files are already here.</summary>
    public void Comparing(Guid deviceId, Guid albumId, ImportChannel channel) => Update(deviceId, albumId, channel, session =>
    {
        session.Comparing = true;
        session.Current = null;
    });

    /// <summary>The phone starts or resumes sending a file, <paramref name="received"/> bytes being already here.</summary>
    public void Receiving(Guid deviceId, Guid albumId, ImportChannel channel, string name, long size, long received,
        int? position = null, int? total = null) => Update(deviceId, albumId, channel, session =>
    {
        session.Comparing = false;
        session.Current = new IncomingTransfer(name, size, received, session.LastActivity, received);
        if (position is not null)
        {
            session.Position = position;
            session.Total = total;
        }
    });

    /// <summary>More bytes of the current file arrived. <see cref="Changed"/> is raised at most twice a second.</summary>
    public void Progress(Guid deviceId, long received)
    {
        lock (_lock)
        {
            if (!_open.TryGetValue(deviceId, out var session) || session.Current is null)
            {
                return;
            }
            var now = clock.GetLocalNow();
            session.Current = session.Current with { Received = received };
            session.LastActivity = now;
            if (now - session.LastNotified < ProgressInterval)
            {
                return;
            }
            session.LastNotified = now;
        }
        Changed?.Invoke();
    }

    public void RecordSaved(Guid deviceId, Guid albumId, ImportChannel channel, bool isVideo) => Update(deviceId, albumId, channel, session =>
    {
        if (isVideo)
        {
            session.Videos++;
        }
        else
        {
            session.Photos++;
        }
        session.Current = null;
    });

    /// <summary>A file arrived but could not be stored. The phone goes on with the others: the session stays open.</summary>
    public void RecordFileError(Guid deviceId, Guid albumId, ImportChannel channel, string name, string message) =>
        Update(deviceId, albumId, channel, session =>
        {
            session.Failures++;
            session.LastFailure = new FileFailure(name, message);
            session.Current = null;
        });

    /// <summary>An error stopped the phone (destination unavailable, disk full…): the session ends with it.</summary>
    public void RecordError(Guid deviceId, Guid albumId, ImportChannel channel, string code, string message)
    {
        BackupSession ended;
        ActiveSession? started;
        lock (_lock)
        {
            var session = OpenFor(deviceId, albumId, channel, out var isNew);
            started = isNew ? session.Snapshot() : null;
            session.ErrorCode = code;
            session.ErrorMessage = message;
            ended = Close(session);
        }
        if (started is not null)
        {
            Started?.Invoke(started);
        }
        Publish(ended);
    }

    /// <summary>
    /// The phone finished its run: the session closes at once instead of waiting for <see cref="IdleTimeout"/>. A phone with
    /// nothing left to send (<paramref name="complete"/>) always gets a session, even with nothing new, so that it can be
    /// announced.
    /// </summary>
    public void RecordFinished(Guid deviceId, Guid albumId, ImportChannel channel, bool complete)
    {
        BackupSession ended;
        lock (_lock)
        {
            if (!_open.TryGetValue(deviceId, out var session))
            {
                if (!complete)
                {
                    return;
                }
                session = OpenFor(deviceId, albumId, channel, out _);
            }
            session.Complete = complete;
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

    private void Update(Guid deviceId, Guid albumId, ImportChannel channel, Action<Open> change)
    {
        ActiveSession? started;
        lock (_lock)
        {
            var session = OpenFor(deviceId, albumId, channel, out var isNew);
            change(session);
            session.LastNotified = session.LastActivity;
            started = isNew ? session.Snapshot() : null;
        }
        if (started is not null)
        {
            Started?.Invoke(started);
        }
        Changed?.Invoke();
    }

    private Open OpenFor(Guid deviceId, Guid albumId, ImportChannel channel, out bool isNew)
    {
        var now = clock.GetLocalNow();
        isNew = !_open.TryGetValue(deviceId, out var session);
        if (session is null)
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
        // An error that stopped the phone says more than the files it skipped before.
        var (code, message) = session.ErrorCode is not null || session.LastFailure is not { } failure
            ? (session.ErrorCode, session.ErrorMessage)
            : (SessionErrors.ImportFailed, FailureSummary(session.Failures, failure));
        var record = new BackupSession(0, session.DeviceId, session.AlbumId, session.Channel, session.StartedAt,
            session.LastActivity, session.Photos, session.Videos, code, message, session.Complete);
        // A connection that brought nothing, failed nothing and finished nothing is not worth a line in the history.
        if (record.Photos + record.Videos == 0 && !record.HasError && !record.Complete)
        {
            return record;
        }
        record = record with { Id = store.Add(record) };
        _last = new FinishedSession(record, session.Failures + (session.ErrorCode is null ? 0 : 1));
        _lastLoaded = true;
        return record;
    }

    private static string FailureSummary(int failures, FileFailure last) => failures <= 1
        ? $"{last.Name} : {last.Message}"
        : $"{failures} fichiers en échec, dont {last.Name} : {last.Message}";

    private void Publish(BackupSession session)
    {
        Changed?.Invoke();
        Closed?.Invoke(session);
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
        public DateTimeOffset LastNotified { get; set; } = startedAt;
        public int Photos { get; set; }
        public int Videos { get; set; }
        public bool Comparing { get; set; }
        public IncomingTransfer? Current { get; set; }
        public int? Position { get; set; }
        public int? Total { get; set; }
        public int Failures { get; set; }
        public FileFailure? LastFailure { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public bool Complete { get; set; }

        public ActiveSession Snapshot() => new(DeviceId, AlbumId, Channel, StartedAt, Photos, Videos, LastActivity, Comparing,
            Current, Position, Total, Failures, LastFailure);
    }
}
