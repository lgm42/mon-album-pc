using System.Globalization;

namespace MonAlbumPc.Core.Library;

/// <summary>A Windows notification about the library.</summary>
public sealed record LibraryNotification(string Title, string Message, bool IsWarning = false);

/// <summary>The words of the notifications about finished backups.</summary>
public static class ActivityNotifications
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string CompleteTitle(string phone) => $"{phone} a été entièrement synchronisé";

    public static string InterruptedTitle(string phone) => $"Sauvegarde de {phone} interrompue";

    /// <summary>The phone went on, but some of its files could not be stored.</summary>
    public static string FailedFilesTitle(string phone) => $"Fichiers de {phone} non sauvegardés";

    /// <summary>"52 photos et 3 vidéos dans l'album Nicolas".</summary>
    public static string Detail(int photos, int videos, string album)
    {
        var parts = new List<string>();
        if (photos > 0) parts.Add(Plural(photos, "photo", "photos"));
        if (videos > 0) parts.Add(Plural(videos, "vidéo", "vidéos"));
        var what = parts.Count == 0 ? "Rien de nouveau" : string.Join(" et ", parts);
        return album.Length == 0 ? what : $"{what} dans l'album {album}";
    }

    public static LibraryNotification For(ActivityEntry entry)
    {
        var phone = entry.Device ?? "Le téléphone";
        if (entry.ErrorCode == Import.SessionErrors.ImportFailed)
        {
            return new LibraryNotification(FailedFilesTitle(entry.Device ?? "ce téléphone"), entry.ErrorMessage ?? "", IsWarning: true);
        }
        if (entry.HasError)
        {
            return new LibraryNotification(InterruptedTitle(entry.Device ?? "ce téléphone"), entry.ErrorMessage ?? "La sauvegarde s'est arrêtée.", IsWarning: true);
        }
        return new LibraryNotification(
            entry.Complete ? CompleteTitle(phone) : "Nouvelles photos disponibles",
            Detail(entry.Photos, entry.Videos, entry.AlbumName));
    }

    /// <summary>
    /// One notification for what a pass over the feed found: Windows only keeps the last one shown, and a PC that was off
    /// must not unroll a whole day. With <paramref name="absent"/>, the lines appeared while nobody was there to see them.
    /// </summary>
    public static LibraryNotification? Summarize(IReadOnlyList<ActivityEntry> entries, bool absent)
    {
        if (entries.Count == 0)
        {
            return null;
        }
        if (entries.Count == 1 && !absent)
        {
            return For(entries[0]);
        }
        var parts = new List<string>();
        var (photos, videos) = (entries.Sum(e => e.Photos), entries.Sum(e => e.Videos));
        if (photos > 0 || videos > 0)
        {
            var news = new List<string>();
            if (photos > 0) news.Add(Plural(photos, "nouvelle photo", "nouvelles photos"));
            if (videos > 0) news.Add(Plural(videos, "nouvelle vidéo", "nouvelles vidéos"));
            parts.Add(string.Join(" et ", news));
        }
        // Where each phone ended up: a backup interrupted, then resumed to the end, is good news only.
        var last = entries.GroupBy(e => e.Device ?? "Le téléphone").Select(g => (Phone: g.Key, Entry: g.MaxBy(e => e.At)!)).ToList();
        parts.AddRange(last.Where(l => l.Entry.Complete).Select(l => $"{l.Phone} entièrement synchronisé"));
        parts.AddRange(last.Where(l => l.Entry.HasError).Select(l => l.Entry.ErrorCode == Import.SessionErrors.ImportFailed
            ? $"fichiers de {l.Phone} non sauvegardés"
            : $"sauvegarde de {l.Phone} interrompue"));
        return new LibraryNotification(
            absent ? "Pendant votre absence" : "Nouvelles photos disponibles",
            parts.Count == 0 ? "De nouvelles sauvegardes sont arrivées." : string.Join(" ; ", parts),
            last.Any(l => l.Entry.HasError));
    }

    private static string Plural(int count, string singular, string plural) =>
        $"{count.ToString("N0", French)} {(count is 0 or 1 ? singular : plural)}";
}
