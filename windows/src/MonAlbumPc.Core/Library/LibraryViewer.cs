using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MonAlbumPc.Core.Activity;

namespace MonAlbumPc.Core.Library;

public enum LibraryState
{
    /// <summary>No answer yet: the first look at the library is still going on.</summary>
    Connecting,
    Available,

    /// <summary>The library does not answer: what is known stays on screen, and the next pass tries again.</summary>
    Unavailable,
}

/// <summary>
/// The viewer side of a shared library: looks at it every minute, off the UI thread, keeps the local albums in line with
/// the published ones, and reads what happened in the activity feed. Everything it needs to remember stays on this PC.
/// </summary>
public sealed class LibraryViewer(CoreServices core, string libraryRoot) : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    public const string Title = "Bibliothèque";

    private const long MaxLogSize = 256 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lock _pollLock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly string _positionsFile = Path.Combine(core.DataDirectory, "library-positions.json");
    private readonly string _logFile = Path.Combine(core.DataDirectory, "viewer.log");
    private Dictionary<string, long>? _positions;
    private string? _savedPositions;
    private bool _absent = true; // nobody saw what happened before this start
    private volatile bool _started;

    public string Root { get; } = libraryRoot;

    public LibraryState State { get; private set; } = LibraryState.Connecting;

    /// <summary>End of the last look at the library, whatever it found.</summary>
    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>When the library stopped answering, while it does not.</summary>
    public DateTimeOffset? UnavailableSince { get; private set; }

    /// <summary>The last thing told about the library's photos, and when.</summary>
    public (LibraryNotification Notification, DateTimeOffset At)? LastNews { get; private set; }

    /// <summary>Raised when <see cref="State"/> changes, from a background thread.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when albums or photos may have changed, from a background thread.</summary>
    public event Action? ContentChanged;

    /// <summary>Raised when there is something to tell the user, from a background thread.</summary>
    public event Action<LibraryNotification>? Notified;

    /// <summary>Looks at the library now, then every <see cref="PollInterval"/>.</summary>
    public void Start()
    {
        if (!_started)
        {
            _started = true;
            _ = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    Poll();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
                {
                    Log($"Erreur : {e.Message}");
                    core.Work.Error($"Lecture de la bibliothèque impossible : {e.Message}");
                }
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
    }

    /// <summary>One look at the library. A network share can take its time to answer: never call this on the UI thread.</summary>
    public void Poll()
    {
        lock (_pollLock)
        {
            // Every minute: shown while it runs (a share can be slow to answer), kept out of the logs.
            using var job = core.Work.Start(Title, $"Vérification de {Root}", silent: true);
            try
            {
                var manifest = LibraryManifest.Read(Root);
                if (manifest is null)
                {
                    _absent = true;
                    SetState(LibraryState.Unavailable);
                    return;
                }

                job.Step("Lecture des albums et des dernières sauvegardes");
                var albumsChanged = LibraryAlbumSync.Apply(core.Albums, manifest, Root, core.Clock);
                var fresh = ReadActivity();
                var reconnected = State != LibraryState.Available;
                SetState(LibraryState.Available);
                if (albumsChanged)
                {
                    core.Work.Info($"Albums mis à jour depuis la bibliothèque : {WorkText.Plural(core.Albums.List().Count, "album", "albums")}");
                }
                if (reconnected || albumsChanged || fresh.Count > 0)
                {
                    core.MediaIndexer.RequestRefresh();
                    ContentChanged?.Invoke();
                }
                if (ActivityNotifications.Summarize(fresh, _absent) is { } notification)
                {
                    Log($"Notification : {notification.Title} — {notification.Message}");
                    LastNews = (notification, core.Clock.GetLocalNow());
                    core.Work.Add(notification.IsWarning ? WorkLevel.Warning : WorkLevel.Info, $"{notification.Title} : {notification.Message}");
                    Notified?.Invoke(notification);
                }
                _absent = false;
            }
            finally
            {
                LastChecked = core.Clock.GetLocalNow();
            }
        }
    }

    private List<ActivityEntry> ReadActivity()
    {
        var firstConnection = false;
        if (_positions is null)
        {
            _positions = LoadPositions();
            firstConnection = _positions is null;
            _positions ??= [];
        }
        List<ActivityEntry> fresh = [];
        if (firstConnection)
        {
            // The past of a library is not news.
            ActivityFeed.SkipToEnd(Root, _positions);
        }
        else
        {
            fresh = ActivityFeed.ReadNew(Root, _positions);
        }
        SavePositions(_positions);
        return fresh;
    }

    private void SetState(LibraryState state)
    {
        if (State != state)
        {
            var previous = State;
            State = state;
            Log(state == LibraryState.Available ? $"Bibliothèque connectée : {Root}" : $"Bibliothèque indisponible : {Root}");
            if (state == LibraryState.Available)
            {
                UnavailableSince = null;
                core.Work.Info(previous == LibraryState.Unavailable ? $"Bibliothèque de nouveau joignable : {Root}" : $"Bibliothèque connectée : {Root}");
            }
            else
            {
                UnavailableSince = core.Clock.GetLocalNow();
                core.Work.Warn($"Bibliothèque injoignable : {Root}");
            }
            StateChanged?.Invoke();
        }
    }

    /// <summary>Saved positions in the feed, or null when this library was never seen from this PC.</summary>
    private Dictionary<string, long>? LoadPositions()
    {
        try
        {
            var saved = JsonSerializer.Deserialize<SavedPositions>(File.ReadAllText(_positionsFile), Json);
            return saved?.Files is not null && string.Equals(saved.Library, Root, StringComparison.OrdinalIgnoreCase) ? saved.Files : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void SavePositions(Dictionary<string, long> positions)
    {
        var json = JsonSerializer.Serialize(new SavedPositions(Root, positions), Json);
        if (json != _savedPositions)
        {
            File.WriteAllText(_positionsFile + ".tmp", json);
            File.Move(_positionsFile + ".tmp", _positionsFile, overwrite: true);
            _savedPositions = json;
        }
    }

    // Small local trace of what the viewer saw and said: notifications leave no other record.
    private void Log(string message)
    {
        try
        {
            if (File.Exists(_logFile) && new FileInfo(_logFile).Length > MaxLogSize)
            {
                File.Move(_logFile, _logFile + ".old", overwrite: true);
            }
            var now = core.Clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            File.AppendAllText(_logFile, $"{now} {message}\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Only a trace.
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
    }

    private sealed record SavedPositions(string Library, Dictionary<string, long> Files);
}
