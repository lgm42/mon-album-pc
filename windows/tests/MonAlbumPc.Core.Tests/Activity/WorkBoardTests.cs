using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Tests.Pairing;

namespace MonAlbumPc.Core.Tests.Activity;

public sealed class WorkBoardTests : IDisposable
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 19, 0, 2, 0, TimeSpan.Zero));
    private readonly WorkBoard _board;

    public WorkBoardTests() => _board = new WorkBoard(_clock, capacity: 3);

    public void Dispose() => _board.Dispose();

    [Fact]
    public void A_job_shows_its_step_and_progress_until_it_finishes_with_an_event()
    {
        var job = _board.Start("Miniatures", "Inventaire de Famille");
        job.Step("Création des miniatures", total: 7018);
        _clock.Advance(TimeSpan.FromSeconds(10));
        job.Progress(1240, item: "IMG_1234.jpg");

        var running = Assert.Single(_board.Running);
        Assert.Equal(("Miniatures", "Création des miniatures", "IMG_1234.jpg", 1240L, (long?)7018), (running.Title, running.Step, running.Item, running.Done, running.Total));
        Assert.Equal(1240 / 7018.0, running.Fraction!.Value, 3);
        Assert.Empty(_board.Events);

        job.Finish("Miniatures : 1 240 créées en 10 s");

        Assert.Empty(_board.Running);
        var e = Assert.Single(_board.Events);
        Assert.Equal(WorkLevel.Info, e.Level);
        Assert.Equal("Miniatures : 1 240 créées en 10 s", e.Text);
        Assert.Equal(_clock.GetLocalNow(), e.At);
    }

    [Fact]
    public void Advance_counts_from_several_threads()
    {
        var job = _board.Start("Miniatures", total: 4000);
        Parallel.For(0, 4000, _ => job.Advance());

        Assert.Equal(4000, Assert.Single(_board.Running).Done);
    }

    [Fact]
    public void A_new_step_starts_its_count_from_zero()
    {
        var job = _board.Start("Index des positions");
        job.Progress(500, item: "a.jpg");
        job.Step("Lecture", total: 20);

        var running = Assert.Single(_board.Running);
        Assert.Equal((0L, (long?)20, (string?)null), (running.Done, running.Total, running.Item));
    }

    [Fact]
    public void A_job_disposed_without_finishing_leaves_no_event()
    {
        using (var job = _board.Start("Miniatures"))
        {
            job.Advance();
        }

        Assert.Empty(_board.Running);
        Assert.Empty(_board.Events);
    }

    [Fact]
    public void A_job_finished_without_note_leaves_no_event_but_is_ended_for_the_logs()
    {
        WorkEnded? ended = null;
        _board.Ended += e => ended = e;

        _board.Start("Miniatures").Finish("Miniatures : 7 018 déjà prêtes en 4 s", note: false);

        Assert.Empty(_board.Events);
        Assert.Equal("Miniatures : 7 018 déjà prêtes en 4 s", ended?.Summary);
    }

    [Fact]
    public void Finishing_twice_counts_once()
    {
        var ends = 0;
        _board.Ended += _ => ends++;
        var job = _board.Start("Miniatures");

        job.Finish("une fois");
        job.Finish("deux fois");
        job.Dispose();

        Assert.Equal(1, ends);
        Assert.Single(_board.Events);
    }

    [Fact]
    public void Warnings_of_a_job_are_capped_but_counted()
    {
        using var board = new WorkBoard(_clock);
        var job = board.Start("Miniatures");
        for (var i = 0; i < 25; i++)
        {
            job.Warn($"Miniature impossible : IMG_{i}.jpg");
        }

        Assert.Equal(25, job.Warnings);
        Assert.Equal(WorkJob.MaxWarnings, board.Events.Count);
        Assert.All(board.Events, e => Assert.Equal(WorkLevel.Warning, e.Level));
    }

    [Fact]
    public void The_recent_events_are_bounded_newest_first()
    {
        for (var i = 1; i <= 5; i++)
        {
            _board.Info($"événement {i}");
        }

        Assert.Equal(["événement 5", "événement 4", "événement 3"], _board.Events.Select(e => e.Text));
    }

    [Fact]
    public void Events_told_by_the_app_go_to_the_logs_but_not_those_taken_from_them()
    {
        List<string> noted = [];
        _board.Noted += e => noted.Add(e.Text);

        _board.Warn("Bibliothèque injoignable : \\\\nas\\Telephones");
        _board.Record(WorkLevel.Error, "venu des logs");

        Assert.Equal(["Bibliothèque injoignable : \\\\nas\\Telephones"], noted);
        Assert.Equal(2, _board.Events.Count);
    }

    [Fact]
    public void Silent_jobs_are_flagged_for_the_logs()
    {
        bool? silent = null;
        _board.Started += (_, s) => silent = s;

        _board.Start("Bibliothèque", silent: true).Dispose();

        Assert.True(silent);
    }

    [Fact]
    public void Changes_are_announced_at_most_twice_a_second()
    {
        var count = 0;
        long seen = 0;
        var job = _board.Start("Miniatures", total: 100_000);
        _board.Changed += () =>
        {
            Interlocked.Increment(ref count);
            Interlocked.Exchange(ref seen, _board.Running.Single().Done);
        };

        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 100_000; i++)
        {
            job.Advance();
        }
        var elapsed = clock.Elapsed;
        Thread.Sleep(WorkBoard.NotifyInterval * 3);

        Assert.InRange(count, 1, (int)Math.Ceiling(elapsed / WorkBoard.NotifyInterval) + 2);
        // The last change is never lost: it comes at the end of the interval.
        Assert.Equal(100_000, Interlocked.Read(ref seen));
    }

    [Fact]
    public void Remaining_time_follows_the_pace_of_the_current_step()
    {
        var start = _clock.GetLocalNow();
        var work = new RunningWork(1, "Miniatures", "Création", null, 100, 400, start, start, start);

        Assert.Null(work.Remaining(start.AddSeconds(2)));
        Assert.Equal(TimeSpan.FromSeconds(60), work.Remaining(start.AddSeconds(20)));
        Assert.Null((work with { Total = null }).Remaining(start.AddSeconds(20)));
    }
}
