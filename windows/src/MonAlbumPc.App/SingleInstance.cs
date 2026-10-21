namespace MonAlbumPc.App;

/// <summary>
/// One instance per PC, all sessions included. A second launch asks the running instance to show its window
/// and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Global\MonAlbumPC.Instance";
    private const string ShowEventName = @"Global\MonAlbumPC.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent, Action onShowRequested)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _registration = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, false);
    }

    /// <summary>
    /// Returns null when another instance already runs (after asking it to show itself). A <paramref name="dataDirectory"/>
    /// other than the default one (development, tests) gets its own lock, so it never wakes up the real app.
    /// </summary>
    public static SingleInstance? TryAcquire(Action onShowRequested, string? dataDirectory = null)
    {
        var suffix = dataDirectory is null
            ? ""
            : "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(dataDirectory).ToUpperInvariant())))[..16];
        var mutex = new Mutex(true, MutexName + suffix, out var createdNew);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + suffix);
        if (!createdNew)
        {
            showEvent.Set();
            showEvent.Dispose();
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, showEvent, onShowRequested);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
