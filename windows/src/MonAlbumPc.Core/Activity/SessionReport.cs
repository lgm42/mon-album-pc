using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Activity;

/// <summary>Puts each backup written to the history in the recent activity: "Sauvegarde de POCO X7 Pro terminée : 20 fichiers".</summary>
public static class SessionReport
{
    public static void Attach(CoreServices core) => core.Sessions.Ended += session =>
    {
        var phone = core.Devices.Get(session.DeviceId)?.Name ?? "téléphone dissocié";
        var album = core.Albums.Get(session.AlbumId)?.Name;
        var (level, text) = Describe(session, phone, album);
        core.Work.Add(level, text);
    };

    public static (WorkLevel Level, string Text) Describe(BackupSession session, string phone, string? album)
    {
        var files = session.Photos + session.Videos;
        var what = files == 0 ? "rien de nouveau" : WorkText.Plural(files, "fichier", "fichiers");
        var where = files > 0 && album is { Length: > 0 } ? $" dans l'album {album}" : "";
        if (session.ErrorCode == SessionErrors.ImportFailed)
        {
            return (WorkLevel.Warning, $"Sauvegarde de {phone} terminée : {what}{where} ; {session.ErrorMessage}");
        }
        if (session.HasError)
        {
            return (WorkLevel.Warning, $"Sauvegarde de {phone} interrompue ({what}) : {session.ErrorMessage}");
        }
        return (WorkLevel.Info, session.Complete && files == 0
            ? $"{phone} est à jour : rien de nouveau"
            : $"Sauvegarde de {phone} terminée : {what}{where}");
    }
}
