using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Library;

/// <summary>
/// Keeps the shared part of a library up to date on the server that owns it: <c>library.json</c> follows the albums, and
/// each finished backup is added to the activity feed. The viewers only read these files.
/// </summary>
public sealed class LibraryPublisher(CoreServices core, string libraryRoot)
{
    private readonly Lock _lock = new();

    /// <summary>Raised when something could not be written (photos volume gone, missing permission).</summary>
    public event Action<string, Exception>? Failed;

    /// <summary>Publishes the albums now, and every change from now on.</summary>
    public void Start()
    {
        core.Albums.Changed += PublishManifest;
        core.Sessions.Ended += PublishActivity;
        PublishManifest();
    }

    private void PublishManifest() => Guarded("library.json", () =>
    {
        lock (_lock)
        {
            LibraryManifest.Write(libraryRoot, LibraryManifest.From(libraryRoot, core.PcName, core.Albums.List()));
        }
    });

    private void PublishActivity(BackupSession session) => Guarded("activity", () =>
    {
        var album = core.Albums.Get(session.AlbumId)?.Name ?? "";
        var device = core.Devices.Get(session.DeviceId)?.Name;
        ActivityFeed.Append(libraryRoot, core.Identity.PcId, ActivityEntry.From(session, album, device));
    });

    // Publishing never gets in the way of a backup: the files are for the viewers, the catalog stays the reference.
    private void Guarded(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke(what, e);
        }
    }
}
