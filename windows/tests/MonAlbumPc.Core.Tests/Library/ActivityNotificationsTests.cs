using MonAlbumPc.Core.Library;

namespace MonAlbumPc.Core.Tests.Library;

public sealed class ActivityNotificationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(1));

    private static ActivityEntry Entry(string? device = "POCO X7 Pro", int photos = 0, int videos = 0, bool complete = false,
        string? error = null, int minutes = 0) =>
        new("id", Now.AddMinutes(minutes), Guid.NewGuid(), "Nicolas", device, photos, videos, complete, error, error is null ? null : "Disque plein");

    [Fact]
    public void A_complete_backup_names_the_phone_and_what_arrived()
    {
        var notification = ActivityNotifications.For(Entry(photos: 52, videos: 3, complete: true));

        Assert.Equal("POCO X7 Pro a été entièrement synchronisé", notification.Title);
        Assert.Equal("52 photos et 3 vidéos dans l'album Nicolas", notification.Message);
        Assert.False(notification.IsWarning);
    }

    [Fact]
    public void An_incomplete_backup_announces_new_photos()
    {
        var notification = ActivityNotifications.For(Entry(photos: 1));

        Assert.Equal("Nouvelles photos disponibles", notification.Title);
        Assert.Equal("1 photo dans l'album Nicolas", notification.Message);
    }

    [Fact]
    public void A_complete_phone_that_brought_nothing_says_so()
    {
        Assert.Equal("Rien de nouveau dans l'album Nicolas", ActivityNotifications.For(Entry(complete: true)).Message);
    }

    [Fact]
    public void An_error_gives_the_reason_as_a_warning()
    {
        var notification = ActivityNotifications.For(Entry(error: "disk_full"));

        Assert.Equal("Sauvegarde de POCO X7 Pro interrompue", notification.Title);
        Assert.Equal("Disque plein", notification.Message);
        Assert.True(notification.IsWarning);
    }

    [Fact]
    public void Files_the_server_could_not_store_are_not_an_interruption()
    {
        var entry = Entry(photos: 3) with { ErrorCode = "import_failed", ErrorMessage = "VID.mp4 : Rangement impossible" };

        var notification = ActivityNotifications.For(entry);

        Assert.Equal("Fichiers de POCO X7 Pro non sauvegardés", notification.Title);
        Assert.Equal("VID.mp4 : Rangement impossible", notification.Message);
        Assert.True(notification.IsWarning);
        Assert.Contains("fichiers de POCO X7 Pro non sauvegardés", ActivityNotifications.Summarize([entry], absent: true)!.Message);
    }

    [Fact]
    public void Nothing_new_gives_no_notification() => Assert.Null(ActivityNotifications.Summarize([], absent: true));

    [Fact]
    public void A_single_line_keeps_its_own_notification_while_the_app_is_watching()
    {
        var notification = ActivityNotifications.Summarize([Entry(photos: 2, complete: true)], absent: false);

        Assert.Equal("POCO X7 Pro a été entièrement synchronisé", notification!.Title);
    }

    [Fact]
    public void What_happened_while_the_pc_was_off_fits_in_one_notification()
    {
        var notification = ActivityNotifications.Summarize(
            [Entry(photos: 100), Entry(photos: 20, complete: true, minutes: 5)], absent: true);

        Assert.Equal("Pendant votre absence", notification!.Title);
        Assert.Equal("120 nouvelles photos ; POCO X7 Pro entièrement synchronisé", notification.Message);
    }

    [Fact]
    public void Even_one_line_is_summarized_after_an_absence()
    {
        var notification = ActivityNotifications.Summarize([Entry(photos: 1, videos: 2)], absent: true);

        Assert.Equal("Pendant votre absence", notification!.Title);
        Assert.Equal("1 nouvelle photo et 2 nouvelles vidéos", notification.Message);
    }

    [Fact]
    public void Several_lines_found_together_are_summarized_too()
    {
        var notification = ActivityNotifications.Summarize(
            [Entry("A", photos: 1), Entry("B", photos: 1, complete: true)], absent: false);

        Assert.Equal("Nouvelles photos disponibles", notification!.Title);
        Assert.Equal("2 nouvelles photos ; B entièrement synchronisé", notification.Message);
    }

    [Fact]
    public void Only_where_each_phone_ended_up_counts()
    {
        var notification = ActivityNotifications.Summarize(
            [Entry("A", error: "disk_full"), Entry("A", complete: true, minutes: 10), Entry("B", photos: 3, error: "disk_full", minutes: 5)], absent: true);

        Assert.Equal("3 nouvelles photos ; A entièrement synchronisé ; sauvegarde de B interrompue", notification!.Message);
        Assert.True(notification.IsWarning);
    }
}
