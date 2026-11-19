using Microsoft.Data.Sqlite;
using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Media;

/// <summary>
/// Refreshes the <see cref="MediaIndex"/> of every album in the background, on a low-priority thread: album folders may
/// be big or sit on a slow network share. Requests made while a refresh runs are merged into one more pass.
/// </summary>
public sealed class MediaIndexer(AlbumStore albums, MediaIndex index, WorkBoard? work = null) : IDisposable
{
    public const string Title = "Index des positions";

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
            var job = work?.Start(Title, "Inventaire des albums");
            try
            {
                var progress = new Reporter(p =>
                {
                    Show(job, p);
                    Progress?.Invoke(p);
                });
                var total = new MediaIndexResult();
                foreach (var album in albums.List())
                {
                    job?.Step($"Inventaire de {album.Name}");
                    try
                    {
                        if (!Directory.Exists(album.DestinationRoot))
                        {
                            job?.Warn($"{Title} : dossier de l'album {album.Name} injoignable ({album.DestinationRoot})");
                        }
                        var result = index.Refresh(album, progress, _stop.Token);
                        changed |= result.Changed;
                        total = new MediaIndexResult(total.Read + result.Read, total.Removed + result.Removed, total.Files + result.Files,
                            total.Tagged + result.Tagged);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
                    {
                        // This album is unreachable for now; the others still get indexed.
                        job?.Warn($"{Title} : album {album.Name} illisible pour l'instant ({e.Message})");
                    }
                }
                job?.Finish(Summary(total, job.Elapsed), job.Warnings > 0 ? WorkLevel.Warning : WorkLevel.Info);
            }
            catch (OperationCanceledException)
            {
                job?.Dispose();
                lock (_lock)
                {
                    _running = false;
                }
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
            {
                // The catalog is out of reach for now: on this thread, letting it through would close the whole app.
                job?.Finish($"{Title} interrompu : catalogue inaccessible ({e.Message})", WorkLevel.Error, error: e);
            }
            Completed?.Invoke(changed);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
    }

    /// <summary>"Index des positions à jour : 7 012 fichiers (24 lus, 2 retirés) en 3 s".</summary>
    public static string Summary(MediaIndexResult total, TimeSpan elapsed)
    {
        List<string> changes = [];
        if (total.Read > 0)
        {
            changes.Add(WorkText.Plural(total.Read, "lu", "lus"));
        }
        if (total.Removed > 0)
        {
            changes.Add(WorkText.Plural(total.Removed, "retiré", "retirés"));
        }
        if (total.Tagged > 0)
        {
            changes.Add($"tags de {WorkText.Plural(total.Tagged, "fichier repris", "fichiers repris")} d'un autre serveur");
        }
        var detail = changes.Count == 0 ? "" : $" ({string.Join(", ", changes)})";
        return $"{Title} à jour : {WorkText.Plural(total.Files, "fichier", "fichiers")}{detail} en {WorkText.Duration(elapsed)}";
    }

    // Listing first (no total yet), then reading the new or changed files.
    private static void Show(WorkJob? job, MediaIndexProgress progress)
    {
        if (job is null)
        {
            return;
        }
        if (progress.Total == 0)
        {
            job.Progress(progress.Done, item: progress.Item);
        }
        else if (progress.Done == 0)
        {
            job.Step($"Lecture des fichiers nouveaux de {progress.AlbumName}", progress.Total);
        }
        else
        {
            job.Progress(progress.Done, progress.Total, progress.Item);
        }
    }

    /// <summary>Synchronous progress (no captured context: listeners marshal to their own thread).</summary>
    private sealed class Reporter(Action<MediaIndexProgress> report) : IProgress<MediaIndexProgress>
    {
        public void Report(MediaIndexProgress value) => report(value);
    }
}
