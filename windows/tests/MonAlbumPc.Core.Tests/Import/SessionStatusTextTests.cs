using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Import;

public sealed class SessionStatusTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 23, 16, 0, TimeSpan.FromHours(1));

    private static ActiveSession Session(IncomingTransfer? current = null, int photos = 0, int videos = 0, int failures = 0,
        FileFailure? failure = null, TimeSpan? silent = null, bool comparing = false, int? position = null, int? total = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), ImportChannel.Wifi, Now.AddMinutes(-5), photos, videos, Now - (silent ?? TimeSpan.Zero),
            comparing, current, position, total, failures, failure);

    [Fact]
    public void A_file_coming_in_shows_its_name_its_bytes_its_pace_and_the_run()
    {
        var file = new IncomingTransfer("VID-20231126-WA0007.mp4", 120L << 20, 45L << 20, Now.AddSeconds(-10), 25L << 20);

        var lines = SessionStatusText.For(Session(file, photos: 17, videos: 1, position: 19, total: 20), "POCO X7 Pro", Now);

        Assert.Equal("Réception depuis POCO X7 Pro", lines.Title);
        Assert.Equal("VID-20231126-WA0007.mp4 (120,0 Mo)", lines.Detail);
        Assert.Equal("45,0 Mo / 120,0 Mo · 2,0 Mo/s · encore 37 s", lines.Transfer);
        Assert.Equal(0.375, lines.FileFraction);
        Assert.Equal("18 reçus · fichier 19 sur 20", lines.Counts);
        Assert.Null(lines.Error);
        Assert.False(lines.Waiting);
    }

    [Fact]
    public void A_silent_phone_is_waited_for()
    {
        var lines = SessionStatusText.For(Session(photos: 3, silent: TimeSpan.FromMinutes(1)), "POCO X7 Pro", Now);

        Assert.Equal("En attente du téléphone…", lines.Title);
        Assert.Equal("POCO X7 Pro · aucun envoi depuis 1 min", lines.Detail);
        Assert.Equal("3 reçus", lines.Counts);
        Assert.True(lines.Waiting);
    }

    [Fact]
    public void Comparison_and_failures_are_told()
    {
        var lines = SessionStatusText.For(Session(comparing: true, failures: 2, failure: new FileFailure("VID.mp4", "Rangement impossible")),
            "POCO X7 Pro", Now);

        Assert.Equal("Comparaison avec les photos déjà reçues…", lines.Detail);
        Assert.Null(lines.Counts);
        Assert.Equal("2 fichiers en échec, dont VID.mp4 — Rangement impossible", lines.Error);
    }

    [Fact]
    public void Tooltip_fits_on_one_line()
    {
        var file = new IncomingTransfer("IMG_1.jpg", 2000, 500);

        Assert.Equal("Réception depuis POCO · IMG_1.jpg 25 % · 18 reçus · 1 échec",
            SessionStatusText.Compact(Session(file, photos: 18, failures: 1), "POCO", Now));
    }

    [Fact]
    public void Summary_of_the_last_backup()
    {
        var session = new BackupSession(1, Guid.NewGuid(), Guid.NewGuid(), ImportChannel.Wifi, Now.AddMinutes(-10), Now,
            18, 2, SessionErrors.ImportFailed, "VID.mp4 : Rangement impossible");

        Assert.Equal("Dernière sauvegarde : 23:16, POCO, 20 fichiers, 1 erreur",
            SessionStatusText.Summary(new FinishedSession(session, null), "POCO", Now));
        Assert.Equal("Dernière sauvegarde : hier à 23:16, POCO, rien de nouveau",
            SessionStatusText.Summary(new FinishedSession(session with { Photos = 0, Videos = 0, ErrorCode = null }, 0), "POCO", Now.AddDays(1)));
    }
}
