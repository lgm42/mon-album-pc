using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using ImageMagick;
using MonAlbumPc.Core.Model;

namespace MonAlbumPc.Core.Media;

/// <summary>What a pass over the albums did. <see cref="FirstError"/> tells why the first failure failed.</summary>
public sealed record ThumbnailSweepResult(int Made, int Removed, int Failed, int Ready, string? FirstError);

/// <summary>
/// Keeps the thumbnails of albums complete: makes the missing and outdated ones, most recent first, and removes those whose
/// photo or video is gone (deleted, moved to the trash). The work runs on a few threads at the lowest priority: the
/// server's processor is shared with phone backups and with other services.
/// </summary>
public sealed class ThumbnailSweeper(int parallelism)
{
    private const int LowestNice = 19;

    // Temporary files left by an interrupted run are removed once surely abandoned.
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(1);

    // Files that could not be decoded, with their date: not tried again until they change.
    private readonly ConcurrentDictionary<string, DateTime> _undecodable = new();

    public ThumbnailSweepResult Sweep(IEnumerable<Album> albums, CancellationToken cancellation)
    {
        var removed = 0;
        var ready = 0;
        List<(string Root, LibraryItem Item)> missing = [];
        foreach (var album in albums)
        {
            cancellation.ThrowIfCancellationRequested();
            removed += RemoveOrphans(album.DestinationRoot);
            foreach (var item in PhotoLibrary.Scan(album).Where(i => ThumbnailMaker.CanMake(i.Path)))
            {
                if (ThumbnailStore.Fresh(album.DestinationRoot, item.Path) is not null)
                {
                    ready++;
                }
                else if (!_undecodable.TryGetValue(item.Path, out var failedAt) || failedAt != File.GetLastWriteTimeUtc(item.Path))
                {
                    missing.Add((album.DestinationRoot, item));
                }
            }
        }

        var queue = new ConcurrentQueue<(string Root, LibraryItem Item)>(
            missing.OrderByDescending(m => m.Item.Month).ThenByDescending(m => m.Item.Date));
        var made = 0;
        var failed = 0;
        string? firstError = null;
        void Work()
        {
            LowerPriority();
            while (!cancellation.IsCancellationRequested && queue.TryDequeue(out var next))
            {
                try
                {
                    ThumbnailMaker.Make(next.Root, next.Item.Path, cancellation);
                    Interlocked.Increment(ref made);
                }
                catch (Exception e) when (e is MagickException or InvalidDataException or TimeoutException)
                {
                    _undecodable[next.Item.Path] = File.GetLastWriteTimeUtc(next.Item.Path);
                    Fail(next.Item.Path, e);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
                {
                    // Gone in the meantime, or out of reach for now: tried again next time.
                    Fail(next.Item.Path, e);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        void Fail(string path, Exception e)
        {
            Interlocked.Increment(ref failed);
            Interlocked.CompareExchange(ref firstError, $"{Path.GetFileName(path)} : {e.Message}", null);
        }

        var workers = Enumerable.Range(0, Math.Min(Math.Max(parallelism, 1), queue.Count))
            .Select(n => new Thread(Work) { IsBackground = true, Name = $"Thumbnails {n + 1}" })
            .ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        cancellation.ThrowIfCancellationRequested();
        return new ThumbnailSweepResult(made, removed, failed, ready, firstError);
    }

    private static int RemoveOrphans(string albumRoot)
    {
        var folder = ThumbnailStore.Folder(albumRoot);
        if (!Directory.Exists(folder))
        {
            return 0;
        }
        var removed = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options).ToList())
        {
            var orphan = file.Name.EndsWith(".jpg", StringComparison.Ordinal)
                ? !File.Exists(Path.Combine(albumRoot, Path.GetRelativePath(folder, file.FullName)[..^".jpg".Length]))
                : DateTime.UtcNow - file.LastWriteTimeUtc > AbandonedAfter;
            if (!orphan)
            {
                continue;
            }
            try
            {
                file.Delete();
                removed++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Tried again next time.
            }
        }
        return removed;
    }

    private static void LowerPriority()
    {
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        // .NET ignores thread priorities on Linux. A Linux thread has its own nice value, inherited by the threads and
        // programs it starts (ImageMagick's OpenMP workers, ffmpeg).
        try
        {
            _ = setpriority(0, gettid(), LowestNice);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // An unusual C library: the work just runs at normal priority.
        }
    }

    [DllImport("libc")]
    private static extern int gettid();

    [DllImport("libc")]
    private static extern int setpriority(int which, int who, int priority);
}
