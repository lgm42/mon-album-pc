using System.Globalization;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.App.History;

/// <summary>Builds the history list: sessions grouped by day, filtered by album, phone and errors.</summary>
public sealed class HistoryViewModel(CoreServices core)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static readonly FilterOption AllAlbums = new(null, "Tous les albums");
    public static readonly FilterOption AllPhones = new(null, "Tous les téléphones");

    public IReadOnlyList<FilterOption> Albums { get; private set; } = [AllAlbums];
    public IReadOnlyList<FilterOption> Phones { get; private set; } = [AllPhones];

    private List<BackupSession> _sessions = [];

    public void Load()
    {
        _sessions = core.SessionHistory.List();
        Albums = [AllAlbums, .. core.Albums.List().Select(a => new FilterOption(a.Id, a.Name))];
        Phones = [AllPhones, .. _sessions.Select(s => s.DeviceId).Distinct()
            .Select(id => new FilterOption(id, core.Devices.Get(id)?.Name ?? "Téléphone dissocié"))];
    }

    public List<DayGroup> Groups(Guid? album, Guid? phone, bool errorsOnly)
    {
        var today = core.Clock.GetLocalNow().Date;
        return _sessions
            .Where(s => (album is null || s.AlbumId == album) && (phone is null || s.DeviceId == phone) && (!errorsOnly || s.HasError))
            .GroupBy(s => s.StartedAt.LocalDateTime.Date)
            .Select(g => new DayGroup(DayTitle(g.Key, today), [.. g.Select(Row)]))
            .ToList();
    }

    private SessionRow Row(BackupSession s)
    {
        var phone = core.Devices.Get(s.DeviceId)?.Name ?? "Téléphone dissocié";
        var album = core.Albums.Get(s.AlbumId)?.Name;
        var summary = Summary(s.Photos, s.Videos);
        var (title, hint) = s.HasError ? Describe(s.ErrorCode!) : (summary, null);
        return new SessionRow(
            s.StartedAt.LocalDateTime.ToString("HH:mm", French),
            phone,
            album is null ? "Album supprimé" : $"Album {album}",
            title,
            s.HasError ? (s.Photos + s.Videos > 0 ? $"{summary} reçus avant l'arrêt. {s.ErrorMessage}" : s.ErrorMessage) : null,
            hint,
            s.Channel == ImportChannel.Usb ? "USB" : "Wi-Fi",
            s.Channel == ImportChannel.Usb ? "" : "",
            s.HasError);
    }

    private static string Summary(int photos, int videos) => (photos, videos) switch
    {
        (0, 0) => "Aucun fichier",
        (_, 0) => Formatting.Plural(photos, "photo", "photos"),
        (0, _) => Formatting.Plural(videos, "vidéo", "vidéos"),
        _ => $"{Formatting.Plural(photos, "photo", "photos")}, {Formatting.Plural(videos, "vidéo", "vidéos")}",
    };

    private static (string Title, string? Hint) Describe(string code) => code switch
    {
        "destination_unavailable" => ("Dossier de destination introuvable",
            "Conseil : rebranchez le disque externe ou allumez le NAS. Le téléphone réessaiera tout seul."),
        "disk_full" => ("Disque plein", "Conseil : libérez de la place sur le disque de destination."),
        _ => ("Sauvegarde interrompue", null),
    };

    private static string DayTitle(DateTime day, DateTime today)
    {
        var name = day.ToString("dddd d MMMM", French);
        if (day == today) return $"Aujourd'hui · {name}";
        if (day == today.AddDays(-1)) return $"Hier · {name}";
        return char.ToUpper(name[0], French) + name[1..];
    }
}

public sealed record FilterOption(Guid? Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record DayGroup(string Title, IReadOnlyList<SessionRow> Sessions);

public sealed record SessionRow(
    string Time,
    string Phone,
    string Album,
    string Title,
    string? Detail,
    string? Hint,
    string Channel,
    string ChannelIcon,
    bool IsError)
{
    public bool HasDetail => Detail is not null;
    public bool HasHint => Hint is not null;
}
