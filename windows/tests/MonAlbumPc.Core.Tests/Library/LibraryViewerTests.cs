using MonAlbumPc.Core.Library;

namespace MonAlbumPc.Core.Tests.Library;

public sealed class LibraryViewerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly CoreServices _core;
    private readonly string _library;
    private readonly Guid _server = Guid.NewGuid();
    private readonly Guid _albumId = Guid.NewGuid();
    private readonly List<LibraryNotification> _notifications = [];

    public LibraryViewerTests()
    {
        _core = CoreServices.Create(_temp.Combine("viewer"));
        _library = _temp.Combine("library");
        Publish(new LibraryAlbum(_albumId, "Nicolas", "#0F6CBD", "Nico"));
    }

    public void Dispose()
    {
        _core.Dispose();
        _temp.Dispose();
    }

    private void Publish(params LibraryAlbum[] albums) => LibraryManifest.Write(_library, new LibraryManifest(1, "NAS", albums));

    private void Activity(int photos, bool complete = false, string device = "POCO X7 Pro") => ActivityFeed.Append(_library, _server,
        new ActivityEntry(Guid.NewGuid().ToString(), DateTimeOffset.Now, _albumId, "Nicolas", device, photos, 0, complete, null, null));

    private LibraryViewer Viewer(string? root = null)
    {
        var viewer = new LibraryViewer(_core, root ?? _library);
        viewer.Notified += _notifications.Add;
        return viewer;
    }

    [Fact]
    public void The_first_connection_brings_the_albums_but_no_notification_for_the_past()
    {
        Activity(500, complete: true);
        var viewer = Viewer();
        var refreshed = 0;
        viewer.ContentChanged += () => refreshed++;
        Assert.Equal(LibraryState.Connecting, viewer.State);

        viewer.Poll();

        Assert.Equal(LibraryState.Available, viewer.State);
        Assert.Equal(1, refreshed);
        var album = Assert.Single(_core.Albums.List());
        Assert.Equal(Path.Combine(_library, "Nico"), album.DestinationRoot);
        Assert.Empty(_notifications);
    }

    [Fact]
    public void A_new_line_gives_a_notification_and_refreshes_the_page()
    {
        var viewer = Viewer();
        viewer.Poll();
        var refreshed = 0;
        viewer.ContentChanged += () => refreshed++;

        viewer.Poll();
        Assert.Equal(0, refreshed);

        Activity(52, complete: true);
        viewer.Poll();

        Assert.Equal(1, refreshed);
        var notification = Assert.Single(_notifications);
        Assert.Equal("POCO X7 Pro a été entièrement synchronisé", notification.Title);
        Assert.Equal("52 photos dans l'album Nicolas", notification.Message);
    }

    [Fact]
    public void A_line_is_announced_only_once()
    {
        var viewer = Viewer();
        viewer.Poll();
        Activity(1);

        viewer.Poll();
        viewer.Poll();

        Assert.Single(_notifications);
    }

    [Fact]
    public void Lines_that_appeared_while_the_app_was_closed_make_one_summary_at_startup()
    {
        Viewer().Poll();
        Activity(100);
        Activity(20, complete: true);

        Viewer().Poll();

        var notification = Assert.Single(_notifications);
        Assert.Equal("Pendant votre absence", notification.Title);
        Assert.Equal("120 nouvelles photos ; POCO X7 Pro entièrement synchronisé", notification.Message);
    }

    [Fact]
    public void An_unreachable_library_keeps_what_is_known_and_recovers_by_itself()
    {
        var viewer = Viewer();
        var states = new List<LibraryState>();
        viewer.StateChanged += () => states.Add(viewer.State);
        viewer.Poll();
        var manifest = LibraryManifest.FilePath(_library);
        File.Move(manifest, manifest + ".away");

        viewer.Poll();

        Assert.Equal(LibraryState.Unavailable, viewer.State);
        Assert.Single(_core.Albums.List());

        Activity(3);
        File.Move(manifest + ".away", manifest);
        var refreshed = 0;
        viewer.ContentChanged += () => refreshed++;
        viewer.Poll();

        Assert.Equal([LibraryState.Available, LibraryState.Unavailable, LibraryState.Available], states);
        Assert.Equal(1, refreshed);
        Assert.Equal("Pendant votre absence", Assert.Single(_notifications).Title);
    }

    [Fact]
    public void What_the_viewer_finds_is_on_the_activity_board()
    {
        var viewer = Viewer();
        viewer.Poll();
        var manifest = LibraryManifest.FilePath(_library);
        File.Move(manifest, manifest + ".away");
        viewer.Poll();

        Assert.NotNull(viewer.UnavailableSince);
        Assert.Equal($"Bibliothèque injoignable : {_library}", _core.Work.Events[0].Text);

        File.Move(manifest + ".away", manifest);
        Activity(3);
        viewer.Poll();

        Assert.Null(viewer.UnavailableSince);
        Assert.NotNull(viewer.LastChecked);
        Assert.Equal("Pendant votre absence", viewer.LastNews?.Notification.Title);
        Assert.Contains(_core.Work.Events, e => e.Text == $"Bibliothèque de nouveau joignable : {_library}");
        Assert.Contains(_core.Work.Events, e => e.Text.StartsWith("Pendant votre absence : 3 nouvelles photos", StringComparison.Ordinal));
        Assert.DoesNotContain(_core.Work.Running, w => w.Title == LibraryViewer.Title);
    }

    [Fact]
    public void A_damaged_manifest_changes_nothing()
    {
        var viewer = Viewer();
        viewer.Poll();
        File.WriteAllText(LibraryManifest.FilePath(_library), "{ cut");

        viewer.Poll();

        Assert.Equal(LibraryState.Unavailable, viewer.State);
        Assert.Single(_core.Albums.List());
    }

    [Fact]
    public void Another_library_is_a_first_connection_again()
    {
        Viewer().Poll();
        var other = _temp.Combine("other");
        LibraryManifest.Write(other, new LibraryManifest(1, "NAS 2", [new LibraryAlbum(_albumId, "Nicolas", "#0F6CBD", "Nico")]));
        ActivityFeed.Append(other, _server, new ActivityEntry("x", DateTimeOffset.Now, _albumId, "Nicolas", "A", 9, 0, true, null, null));

        Viewer(other).Poll();

        Assert.Empty(_notifications);
        Assert.Equal(Path.Combine(other, "Nico"), Assert.Single(_core.Albums.List()).DestinationRoot);
    }

    [Fact]
    public void Album_changes_on_the_nas_refresh_the_page()
    {
        var viewer = Viewer();
        viewer.Poll();
        var refreshed = 0;
        viewer.ContentChanged += () => refreshed++;

        Publish(new LibraryAlbum(_albumId, "Nico", "#0F6CBD", "Nico"));
        viewer.Poll();

        Assert.Equal(1, refreshed);
        Assert.Equal("Nico", Assert.Single(_core.Albums.List()).Name);
        Assert.Empty(_notifications);
    }

    [Fact]
    public async Task Start_looks_at_the_library_at_once_off_the_calling_thread()
    {
        using var viewer = Viewer();
        var available = new TaskCompletionSource();
        viewer.StateChanged += () => available.TrySetResult();

        viewer.Start();

        await available.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(LibraryState.Available, viewer.State);
    }

    [Fact]
    public void What_the_viewer_said_is_kept_in_a_local_log()
    {
        var viewer = Viewer();
        viewer.Poll();
        Activity(2);
        viewer.Poll();

        Assert.Contains("Nouvelles photos disponibles", File.ReadAllText(Path.Combine(_core.DataDirectory, "viewer.log")));
    }
}
