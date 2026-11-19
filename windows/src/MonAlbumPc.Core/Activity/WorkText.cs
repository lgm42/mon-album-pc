using System.Globalization;

namespace MonAlbumPc.Core.Activity;

/// <summary>Words of the background work, shared by the Windows app and the NAS administration.</summary>
public static class WorkText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>"1 240".</summary>
    public static string Count(long value) => value.ToString("N0", French);

    public static string Plural(long count, string singular, string plural) =>
        $"{Count(count)} {(count is 0 or 1 ? singular : plural)}";

    /// <summary>"0 s", "45 s", "2 min 10 s", "1 h 05 min".</summary>
    public static string Duration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return $"{(int)elapsed.TotalSeconds} s";
        }
        if (elapsed < TimeSpan.FromHours(1))
        {
            return elapsed.Seconds == 0 ? $"{(int)elapsed.TotalMinutes} min" : $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s";
        }
        return $"{(int)elapsed.TotalHours} h {elapsed.Minutes:00} min";
    }

    /// <summary>"00:04", in the time zone of <paramref name="now"/>, with the day when it is not today: "18/11 23:52".</summary>
    public static string Time(DateTimeOffset at, DateTimeOffset now)
    {
        var local = at.ToOffset(now.Offset);
        return local.Date == now.Date ? local.ToString("HH:mm", French) : local.ToString("dd/MM HH:mm", French);
    }

    /// <summary>"1 240 / 7 018", "1 240" when the total is unknown, null before anything is counted.</summary>
    public static string? Counts(RunningWork work) => work switch
    {
        { Total: > 0 } => $"{Count(work.Done)} / {Count(work.Total.Value)}",
        { Done: > 0 } => Count(work.Done),
        _ => null,
    };

    /// <summary>One line for a status card or a tooltip: "Miniatures · 1 240 / 7 018 · IMG_1234.jpg".</summary>
    public static string Compact(RunningWork work)
    {
        var parts = new List<string> { work.Title };
        if (Counts(work) is { } counts)
        {
            parts.Add(counts);
        }
        else if (work.Step is { } step)
        {
            parts.Add(step);
        }
        if (work.Item is { } item)
        {
            parts.Add(item);
        }
        return string.Join(" · ", parts);
    }

    /// <summary>"1 240 / 7 018 · 18 % · depuis 2 min 10 s · encore 9 min".</summary>
    public static string Pace(RunningWork work, DateTimeOffset now)
    {
        var parts = new List<string>();
        if (Counts(work) is { } counts)
        {
            parts.Add(counts);
        }
        if (work.Fraction is { } fraction)
        {
            parts.Add($"{(int)(fraction * 100)} %");
        }
        parts.Add($"depuis {Duration(work.Elapsed(now))}");
        if (work.Remaining(now) is { } remaining)
        {
            parts.Add($"encore {Duration(Round(remaining))}");
        }
        return string.Join(" · ", parts);
    }

    /// <summary>"00:04 · Miniatures : 1 240 créées".</summary>
    public static string Line(WorkEvent e, DateTimeOffset now) => $"{Time(e.At, now)} · {e.Text}";

    // An estimate to the second looks more precise than it is.
    private static TimeSpan Round(TimeSpan estimate) => estimate < TimeSpan.FromMinutes(1)
        ? TimeSpan.FromSeconds(Math.Ceiling(estimate.TotalSeconds / 5) * 5)
        : TimeSpan.FromMinutes(Math.Ceiling(estimate.TotalMinutes));
}
