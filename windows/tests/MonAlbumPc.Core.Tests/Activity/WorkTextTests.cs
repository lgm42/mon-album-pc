using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Activity;

public sealed class WorkTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 0, 12, 0, TimeSpan.FromHours(1));

    // French digit grouping uses a narrow no-break space.
    private static string Fr(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"(?<=\d) (?=\d{3}\b)", " ");

    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(45, "45 s")]
    [InlineData(130, "2 min 10 s")]
    [InlineData(120, "2 min")]
    [InlineData(3900, "1 h 05 min")]
    public void Durations(int seconds, string expected) => Assert.Equal(expected, WorkText.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Times_carry_the_day_only_when_it_is_not_today()
    {
        Assert.Equal("00:04", WorkText.Time(Now.AddMinutes(-8), Now));
        Assert.Equal("18/11 23:52", WorkText.Time(Now.AddMinutes(-20), Now));
    }

    [Fact]
    public void A_job_in_one_line()
    {
        var work = new RunningWork(1, "Miniatures", "Création des miniatures", "IMG_1234.jpg", 1240, 7018, Now, Now, Now);

        Assert.Equal(Fr("Miniatures · 1 240 / 7 018 · IMG_1234.jpg"), WorkText.Compact(work));
        Assert.Equal("Bibliothèque · Vérification de \\\\nas\\Telephones",
            WorkText.Compact(new RunningWork(2, "Bibliothèque", "Vérification de \\\\nas\\Telephones", null, 0, null, Now, Now, Now)));
    }

    [Fact]
    public void The_pace_of_a_job()
    {
        var start = Now.AddMinutes(-2).AddSeconds(-10);
        var work = new RunningWork(1, "Miniatures", "Création", null, 1240, 7018, start, start, Now);

        Assert.Equal(Fr("1 240 / 7 018 · 17 % · depuis 2 min 10 s · encore 11 min"), WorkText.Pace(work, Now));
    }

    [Fact]
    public void An_event_line() =>
        Assert.Equal("00:04 · Miniatures : 3 créées en 2 s", WorkText.Line(new WorkEvent(Now.AddMinutes(-8), WorkLevel.Info, "Miniatures : 3 créées en 2 s"), Now));

    [Fact]
    public void Thumbnail_summaries()
    {
        Assert.Equal(Fr("Miniatures : 1 240 créées, 3 en échec en 2 min 10 s"),
            new ThumbnailSweepResult(1240, 0, 3, 5000, "IMG_1.heic : illisible").Summary(TimeSpan.FromSeconds(130)));
        Assert.Equal(Fr("Miniatures : 7 018 déjà prêtes en 4 s"), new ThumbnailSweepResult(0, 0, 0, 7018, null).Summary(TimeSpan.FromSeconds(4)));
        Assert.Equal("Miniatures : 1 supprimée en 0 s", new ThumbnailSweepResult(0, 1, 0, 2, null).Summary(TimeSpan.Zero));
    }

    [Fact]
    public void Index_summaries()
    {
        Assert.Equal(Fr("Index des positions à jour : 7 012 fichiers en 3 s"), MediaIndexer.Summary(new MediaIndexResult(0, 0, 7012), TimeSpan.FromSeconds(3)));
        Assert.Equal("Index des positions à jour : 26 fichiers (24 lus, 2 retirés) en 1 min",
            MediaIndexer.Summary(new MediaIndexResult(24, 2, 26), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Session_summaries()
    {
        var start = Now.AddMinutes(-3);
        var session = new BackupSession(1, Guid.NewGuid(), Guid.NewGuid(), ImportChannel.Wifi, start, Now, 12, 8, null, null);

        Assert.Equal((WorkLevel.Info, "Sauvegarde de POCO X7 Pro terminée : 20 fichiers dans l'album Famille"),
            SessionReport.Describe(session, "POCO X7 Pro", "Famille"));
        Assert.Equal((WorkLevel.Info, "POCO X7 Pro est à jour : rien de nouveau"),
            SessionReport.Describe(session with { Photos = 0, Videos = 0, Complete = true }, "POCO X7 Pro", "Famille"));
        Assert.Equal((WorkLevel.Warning, "Sauvegarde de POCO X7 Pro interrompue (rien de nouveau) : Le dossier de l'album est introuvable."),
            SessionReport.Describe(session with { Photos = 0, Videos = 0, ErrorCode = "destination_unavailable", ErrorMessage = "Le dossier de l'album est introuvable." },
                "POCO X7 Pro", "Famille"));
        Assert.Equal((WorkLevel.Warning, "Sauvegarde de POCO X7 Pro terminée : 20 fichiers dans l'album Famille ; IMG_1.jpg : disque plein"),
            SessionReport.Describe(session with { ErrorCode = SessionErrors.ImportFailed, ErrorMessage = "IMG_1.jpg : disque plein" }, "POCO X7 Pro", "Famille"));
    }

    [Fact]
    public void Each_backup_written_to_the_history_lands_on_the_board()
    {
        using var temp = new TempDirectory();
        using var core = CoreServices.Create(temp.Combine("data"));
        var album = new Album(Guid.NewGuid(), "Famille", temp.Combine("Famille"), Album.Palette[0], DateTimeOffset.Now);
        core.Albums.Add(album);
        var phone = Guid.NewGuid();

        core.Sessions.RecordSaved(phone, album.Id, ImportChannel.Wifi, isVideo: false);
        core.Sessions.RecordFinished(phone, album.Id, ImportChannel.Wifi, complete: true);

        Assert.Contains(core.Work.Events, e => e.Text == "Sauvegarde de téléphone dissocié terminée : 1 fichier dans l'album Famille");
    }
}
