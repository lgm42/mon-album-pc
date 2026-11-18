using System.Globalization;

namespace MonAlbumPc.Core.Import;

/// <summary>The status card of a backup in progress: what the phone is doing, how far it is, what failed.</summary>
/// <param name="Detail">"VID-20231126-WA0007.mp4 (120,0 Mo)", or what the phone does between files.</param>
/// <param name="Transfer">"45,0 Mo / 120,0 Mo · 2,1 Mo/s · encore 36 s" while a file comes in.</param>
/// <param name="FileFraction">Share of the current file already received (0 to 1), for its progress bar.</param>
/// <param name="Counts">"18 reçus · fichier 19 sur 20".</param>
/// <param name="Error">Last file the server could not store, with the reason.</param>
/// <param name="Waiting">The phone has been silent for a while (<see cref="ActiveSession.IsWaiting"/>).</param>
public sealed record SessionStatusLines(
    string Title,
    string? Detail,
    string? Transfer,
    double? FileFraction,
    string? Counts,
    string? Error,
    bool Waiting);

/// <summary>Words of the status cards of the Windows app and of the NAS administration, so both tell the same story.</summary>
public static class SessionStatusText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// "Réception depuis POCO X7 Pro", the file and its bytes, "18 reçus · fichier 19 sur 20"; or "En attente du
    /// téléphone…" once it has been silent for a while.
    /// </summary>
    public static SessionStatusLines For(ActiveSession session, string phone, DateTimeOffset now)
    {
        var waiting = session.IsWaiting(now);
        var current = session.Current;
        string? detail;
        if (waiting)
        {
            detail = $"{phone} · aucun envoi depuis {Duration(now - session.LastActivity)}";
            if (current is not null)
            {
                detail += $"{Environment.NewLine}{FileLine(current)}";
            }
        }
        else
        {
            detail = session.Comparing ? "Comparaison avec les photos déjà reçues…" : current is null ? null : FileLine(current);
        }
        return new SessionStatusLines(
            waiting ? "En attente du téléphone…" : $"Réception depuis {phone}",
            detail,
            current is null ? null : Transfer(current, waiting ? null : session.BytesPerSecond),
            current is { Size: > 0 } ? Math.Clamp(current.Received / (double)current.Size, 0, 1) : null,
            Counts(session),
            session.LastFailure is { } failure ? Failure(session.Failures, failure) : null,
            waiting);
    }

    /// <summary>One line, for a tooltip: "Réception depuis POCO X7 Pro · VID-….mp4 45 % · 18 reçus".</summary>
    public static string Compact(ActiveSession session, string phone, DateTimeOffset now)
    {
        var parts = new List<string> { session.IsWaiting(now) ? $"En attente de {phone}…" : $"Réception depuis {phone}" };
        if (session.Current is { } current)
        {
            parts.Add(current.Size > 0 ? $"{current.Name} {current.Received * 100 / current.Size} %" : current.Name);
        }
        if (session.Received > 0)
        {
            parts.Add(Plural(session.Received, "reçu", "reçus"));
        }
        if (session.Failures > 0)
        {
            parts.Add(Plural(session.Failures, "échec", "échecs"));
        }
        return string.Join(" · ", parts);
    }

    /// <summary>"Dernière sauvegarde : 23:16, POCO X7 Pro, 20 fichiers, 1 erreur".</summary>
    public static string Summary(FinishedSession last, string phone, DateTimeOffset now)
    {
        var session = last.Session;
        var parts = new List<string> { When(session.EndedAt, now), phone };
        var files = session.Photos + session.Videos;
        parts.Add(files == 0 ? "rien de nouveau" : Plural(files, "fichier", "fichiers"));
        var errors = last.Errors ?? (session.HasError ? 1 : 0);
        if (errors > 0)
        {
            parts.Add(Plural(errors, "erreur", "erreurs"));
        }
        return $"Dernière sauvegarde : {string.Join(", ", parts)}";
    }

    /// <summary>"VID-20231126-WA0007.mp4 (120,0 Mo)".</summary>
    public static string FileLine(IncomingTransfer file) => $"{file.Name} ({Size(file.Size)})";

    /// <summary>"45,0 Mo / 120,0 Mo · 2,1 Mo/s · encore 36 s"; the pace only when known.</summary>
    public static string Transfer(IncomingTransfer file, double? bytesPerSecond)
    {
        var text = $"{Size(file.Received)} / {Size(file.Size)}";
        if (bytesPerSecond is not { } pace || pace <= 0)
        {
            return text;
        }
        text += $" · {Size((long)pace)}/s";
        var left = file.Size - file.Received;
        return left > 0 ? $"{text} · encore {Duration(TimeSpan.FromSeconds(left / pace))}" : text;
    }

    /// <summary>"1,3 Mo".</summary>
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.0", French) + " Go",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", French) + " Mo",
        _ => $"{Math.Max(bytes > 0 ? 1 : 0, bytes / 1024)} Ko",
    };

    private static string? Counts(ActiveSession session)
    {
        var parts = new List<string>();
        if (session.Received > 0)
        {
            parts.Add(Plural(session.Received, "reçu", "reçus"));
        }
        if (session is { Position: { } position, Total: { } total } && total > 0)
        {
            parts.Add($"fichier {Math.Min(position, total).ToString("N0", French)} sur {total.ToString("N0", French)}");
        }
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Failure(int failures, FileFailure last) => failures <= 1
        ? $"{last.Name} — {last.Message}"
        : $"{Plural(failures, "fichier", "fichiers")} en échec, dont {last.Name} — {last.Message}";

    private static string Duration(TimeSpan elapsed) => elapsed < TimeSpan.FromMinutes(1)
        ? $"{Math.Max(0, (int)elapsed.TotalSeconds)} s"
        : elapsed < TimeSpan.FromHours(1) ? $"{(int)elapsed.TotalMinutes} min" : $"{(int)elapsed.TotalHours} h";

    /// <summary>"23:16", "hier à 23:16", "le 12/11 à 23:16".</summary>
    private static string When(DateTimeOffset at, DateTimeOffset now)
    {
        // In the time zone of the caller's clock (the local one).
        var local = at.ToOffset(now.Offset);
        var time = local.ToString("HH:mm", French);
        var today = now.Date;
        if (local.Date == today) return time;
        if (local.Date == today.AddDays(-1)) return $"hier à {time}";
        return $"le {local.ToString("dd/MM", French)} à {time}";
    }

    private static string Plural(long count, string singular, string plural) =>
        $"{count.ToString("N0", French)} {(count is 0 or 1 ? singular : plural)}";
}
