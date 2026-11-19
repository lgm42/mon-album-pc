using System.Threading.Channels;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Media;

namespace MonAlbumPc.Nas;

/// <summary>
/// Keeps the thumbnails of every album ready for the viewers: a pass shortly after startup, every hour, and after each
/// backup for its album. The work runs at the lowest priority on one or two threads: the NAS also runs other services.
/// </summary>
public sealed class ThumbnailKeeper(CoreServices core, ILogger<ThumbnailKeeper> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    // Phones that connect at startup come first.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    private readonly ThumbnailSweeper _sweeper = new(Math.Clamp(Environment.ProcessorCount / 4, 1, 2));

    // An album to look at, or null for all of them.
    private readonly Channel<Guid?> _requests = Channel.CreateUnbounded<Guid?>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ThumbnailMaker.Ffmpeg is null)
        {
            logger.LogWarning("ffmpeg introuvable : les vidéos n'auront pas de miniature");
        }
        core.Sessions.Ended += session => _requests.Writer.TryWrite(session.AlbumId);
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            _requests.Writer.TryWrite(null);
            _ = RepeatAsync(stoppingToken);
            while (await _requests.Reader.WaitToReadAsync(stoppingToken))
            {
                // Requests that piled up during a pass are merged into one.
                HashSet<Guid>? albums = [];
                while (_requests.Reader.TryRead(out var album))
                {
                    if (album is null)
                    {
                        albums = null;
                    }
                    else
                    {
                        albums?.Add(album.Value);
                    }
                }
                await Task.Factory.StartNew(() => Sweep(albums, stoppingToken), stoppingToken,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException)
        {
            // The container is stopping.
        }
    }

    /// <summary>One pass over <paramref name="only"/> these albums, or all of them when null.</summary>
    private void Sweep(HashSet<Guid>? only, CancellationToken stoppingToken)
    {
        using var job = core.Work.Start(ThumbnailSweepResult.Title, only is null ? "Tous les albums" : "Albums de la dernière sauvegarde");
        try
        {
            var albums = core.Albums.List().Where(a => only is null || only.Contains(a.Id)).ToList();
            var result = _sweeper.Sweep(albums, stoppingToken, job);
            // The hourly pass that finds everything ready goes to the logs only.
            job.Finish(result.Summary(job.Elapsed), result.Failed > 0 ? WorkLevel.Warning : WorkLevel.Info, note: result.Changed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // An exception leaving a background service stops the whole server: the phones matter more than thumbnails.
            job.Finish($"{ThumbnailSweepResult.Title} : passage interrompu ({e.Message})", WorkLevel.Error, error: e);
        }
    }

    private async Task RepeatAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _requests.Writer.TryWrite(null);
            }
        }
        catch (OperationCanceledException)
        {
            // The container is stopping.
        }
    }
}
