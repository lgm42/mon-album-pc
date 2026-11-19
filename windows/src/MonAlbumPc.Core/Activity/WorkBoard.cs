namespace MonAlbumPc.Core.Activity;

public enum WorkLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>One line of the recent activity: "Miniatures : 1 240 créées en 2 min 10 s".</summary>
public sealed record WorkEvent(DateTimeOffset At, WorkLevel Level, string Text);

/// <summary>A background task in progress, as shown on screen.</summary>
/// <param name="Step">What it does right now: "Création des miniatures", "Inventaire de Famille".</param>
/// <param name="Item">The file or folder it is on, when it tells.</param>
/// <param name="Done">Progress within <paramref name="Step"/>, out of <paramref name="Total"/> when known.</param>
public sealed record RunningWork(
    long Id,
    string Title,
    string? Step,
    string? Item,
    long Done,
    long? Total,
    DateTimeOffset StartedAt,
    DateTimeOffset StepStartedAt,
    DateTimeOffset LastProgressAt)
{
    // Below this, a pace says more about the start-up of the task than about the task.
    private static readonly TimeSpan MeasuredAfter = TimeSpan.FromSeconds(5);

    public double? Fraction => Total is > 0 ? Math.Clamp(Done / (double)Total.Value, 0, 1) : null;

    public TimeSpan Elapsed(DateTimeOffset now) => now - StartedAt;

    /// <summary>Time left in the current step at the pace so far, once there is enough to measure.</summary>
    public TimeSpan? Remaining(DateTimeOffset now)
    {
        var spent = now - StepStartedAt;
        if (Total is not { } total || Done <= 0 || Done >= total || spent < MeasuredAfter)
        {
            return null;
        }
        return TimeSpan.FromSeconds(spent.TotalSeconds * (total - Done) / Done);
    }
}

/// <summary>How a task ended, for the logs. <see cref="Silent"/> tasks are only logged when they went wrong.</summary>
public sealed record WorkEnded(RunningWork Work, string? Summary, WorkLevel Level, bool Silent, Exception? Error);

/// <summary>
/// What the app or the server is doing in the background, for the screens that must never look frozen: the tasks in
/// progress (thumbnails, index, library…) and a bounded list of what happened recently. Thread-safe; <see cref="Changed"/>
/// is raised at most twice a second however fast the tasks report.
/// </summary>
public sealed class WorkBoard(TimeProvider clock, int capacity = WorkBoard.DefaultCapacity) : IDisposable
{
    public const int DefaultCapacity = 300;

    public static readonly TimeSpan NotifyInterval = TimeSpan.FromMilliseconds(500);

    private readonly Lock _lock = new();
    private readonly List<WorkJob> _running = [];
    private readonly LinkedList<WorkEvent> _events = [];
    private long _nextId;
    private long _lastNotified = long.MinValue;
    private Timer? _pending;
    private bool _disposed;

    public TimeProvider Clock { get; } = clock;

    /// <summary>Raised from any thread when tasks or events changed, at most every <see cref="NotifyInterval"/>.</summary>
    public event Action? Changed;

    /// <summary>Raised when a task starts, for the logs.</summary>
    public event Action<RunningWork, bool>? Started;

    /// <summary>Raised when a task ends, for the logs.</summary>
    public event Action<WorkEnded>? Ended;

    /// <summary>Raised with each event told by the app itself (not those taken from its logs), for the logs.</summary>
    public event Action<WorkEvent>? Noted;

    /// <summary>The tasks in progress, oldest first.</summary>
    public IReadOnlyList<RunningWork> Running
    {
        get
        {
            lock (_lock)
            {
                return [.. _running.Select(j => j.Snapshot())];
            }
        }
    }

    /// <summary>What happened recently, newest first.</summary>
    public IReadOnlyList<WorkEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>
    /// Starts a task. A <paramref name="silent"/> one is shown while it runs but leaves no trace in the logs unless it
    /// fails: for frequent small work such as the look at the shared library every minute.
    /// </summary>
    public WorkJob Start(string title, string? step = null, long? total = null, bool silent = false)
    {
        WorkJob job;
        lock (_lock)
        {
            job = new WorkJob(this, ++_nextId, title, step, total, Clock.GetLocalNow(), silent);
            _running.Add(job);
        }
        Started?.Invoke(job.Snapshot(), silent);
        Touch();
        return job;
    }

    /// <summary>Adds an event to the recent activity, and to the logs.</summary>
    public void Add(WorkLevel level, string text)
    {
        var added = Record(level, text);
        Noted?.Invoke(added);
    }

    public void Info(string text) => Add(WorkLevel.Info, text);

    public void Warn(string text) => Add(WorkLevel.Warning, text);

    public void Error(string text) => Add(WorkLevel.Error, text);

    /// <summary>Adds an event that already went to the logs (a warning caught from them).</summary>
    public WorkEvent Record(WorkLevel level, string text)
    {
        var added = new WorkEvent(Clock.GetLocalNow(), level, text);
        lock (_lock)
        {
            _events.AddFirst(added);
            while (_events.Count > Math.Max(capacity, 1))
            {
                _events.RemoveLast();
            }
        }
        Touch();
        return added;
    }

    internal void End(WorkJob job, string? summary, WorkLevel level, bool note, Exception? error)
    {
        lock (_lock)
        {
            if (!_running.Remove(job))
            {
                return;
            }
        }
        if (summary is not null && note)
        {
            Record(level, summary);
        }
        else
        {
            Touch();
        }
        Ended?.Invoke(new WorkEnded(job.Snapshot(), summary, level, job.Silent, error));
    }

    internal void NoteFromJob(WorkLevel level, string text) => Add(level, text);

    /// <summary>Something changed: tell the screens now, or once the interval since the last time has passed.</summary>
    internal void Touch()
    {
        lock (_lock)
        {
            if (_disposed || _pending is not null)
            {
                return; // already due
            }
            var now = Environment.TickCount64;
            var wait = _lastNotified == long.MinValue ? 0 : _lastNotified + (long)NotifyInterval.TotalMilliseconds - now;
            if (wait > 0)
            {
                _pending = new Timer(_ => Notify(), null, wait, Timeout.Infinite);
                return;
            }
            _lastNotified = now;
        }
        Changed?.Invoke();
    }

    private void Notify()
    {
        lock (_lock)
        {
            _pending?.Dispose();
            _pending = null;
            if (_disposed)
            {
                return;
            }
            _lastNotified = Environment.TickCount64;
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _pending?.Dispose();
            _pending = null;
        }
    }
}

/// <summary>
/// A task on the <see cref="WorkBoard"/>: tells its step and progress as it goes, then <see cref="Finish"/>es with what
/// it did. Disposing it without finishing (cancellation) just takes it off the board. Safe to call from several threads.
/// </summary>
public sealed class WorkJob : IDisposable
{
    /// <summary>Warnings of one task beyond this count only show in its summary: a thousand failed files are one problem.</summary>
    public const int MaxWarnings = 10;

    private readonly WorkBoard _board;
    private readonly Lock _lock = new();
    private string? _step;
    private string? _item;
    private long _done;
    private long? _total;
    private DateTimeOffset _stepStartedAt;
    private DateTimeOffset _lastProgressAt;
    private int _warnings;
    private bool _finished;

    internal WorkJob(WorkBoard board, long id, string title, string? step, long? total, DateTimeOffset now, bool silent)
    {
        _board = board;
        Id = id;
        Title = title;
        Silent = silent;
        StartedAt = now;
        _step = step;
        _total = total;
        _stepStartedAt = now;
        _lastProgressAt = now;
    }

    public long Id { get; }
    public string Title { get; }
    public bool Silent { get; }
    public DateTimeOffset StartedAt { get; }

    public TimeSpan Elapsed => _board.Clock.GetLocalNow() - StartedAt;

    /// <summary>Warnings told so far, including those beyond <see cref="MaxWarnings"/>.</summary>
    public int Warnings
    {
        get { lock (_lock) { return _warnings; } }
    }

    /// <summary>Moves on to another step; its progress starts from zero.</summary>
    public void Step(string step, long? total = null, string? item = null) => Change(() =>
    {
        _step = step;
        _total = total;
        _item = item;
        _done = 0;
        _stepStartedAt = _lastProgressAt;
    });

    public void Progress(long done, long? total = null, string? item = null) => Change(() =>
    {
        _done = done;
        if (total is not null)
        {
            _total = total;
        }
        if (item is not null)
        {
            _item = item;
        }
    });

    /// <summary>One more done, optionally naming it.</summary>
    public void Advance(string? item = null) => Change(() =>
    {
        _done++;
        if (item is not null)
        {
            _item = item;
        }
    });

    /// <summary>Names what the task is on now, without counting it.</summary>
    public void Working(string item) => Change(() => _item = item);

    /// <summary>A problem that does not stop the task (a file that could not be read…).</summary>
    public void Warn(string text)
    {
        lock (_lock)
        {
            if (_finished || ++_warnings > MaxWarnings)
            {
                return;
            }
        }
        _board.NoteFromJob(WorkLevel.Warning, text);
    }

    /// <summary>
    /// Ends the task with what it did: shown in the recent activity unless <paramref name="note"/> is false (nothing worth
    /// telling), and written to the logs.
    /// </summary>
    public void Finish(string? summary = null, WorkLevel level = WorkLevel.Info, bool note = true, Exception? error = null)
    {
        lock (_lock)
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
        }
        _board.End(this, summary, level, note, error);
    }

    public void Dispose() => Finish();

    internal RunningWork Snapshot()
    {
        lock (_lock)
        {
            return new RunningWork(Id, Title, _step, _item, _done, _total, StartedAt, _stepStartedAt, _lastProgressAt);
        }
    }

    private void Change(Action change)
    {
        lock (_lock)
        {
            if (_finished)
            {
                return;
            }
            _lastProgressAt = _board.Clock.GetLocalNow();
            change();
        }
        _board.Touch();
    }
}
