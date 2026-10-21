using Microsoft.Data.Sqlite;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Media;

/// <summary>
/// Refreshes the <see cref="MediaIndex"/> of every album in the background, on a low-priority thread: album folders may
/// be big or sit on a slow network share. Requests made while a refresh runs are merged into one more pass.
/// </summary>
public sealed class MediaIndexer(AlbumStore albums, MediaIndex index) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private bool _pending;
    private bool _running;

    /// <summary>Raised from the indexing thread while new or changed files are read.</summary>
    public event Action<MediaIndexProgress>? Progress;

    /// <summary>Raised from the indexing thread at the end of a pass; true when the catalog changed.</summary>
    public event Action<bool>? Completed;

    public bool IsRunning
    {
        get { lock (_lock) { return _running; } }
    }

    /// <summary>Asks for a refresh of every album; returns at once.</summary>
    public void RequestRefresh()
    {
        lock (_lock)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }
            _pending = true;
            if (_running)
            {
                return;
            }
            _running = true;
        }
        new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Media index" }.Start();
    }

    private void Run()
    {
        while (true)
        {
            lock (_lock)
            {
                if (!_pending || _stop.IsCancellationRequested)
                {
                    _running = false;
                    return;
                }
                _pending = false;
            }
            var changed = false;
            try
            {
                var progress = new Reporter(p => Progress?.Invoke(p));
                foreach (var album in albums.List())
                {
                    try
                    {
                        changed |= index.Refresh(album, progress, _stop.Token).Changed;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
                    {
                        // This album is unreachable for now; the others still get indexed.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (_lock)
                {
                    _running = false;
                }
                return;
            }
            Completed?.Invoke(changed);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
    }

    /// <summary>Synchronous progress (no captured context: listeners marshal to their own thread).</summary>
    private sealed class Reporter(Action<MediaIndexProgress> report) : IProgress<MediaIndexProgress>
    {
        public void Report(MediaIndexProgress value) => report(value);
    }
}
