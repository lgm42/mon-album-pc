using System.Net;
using Microsoft.Win32;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Library;
using MonAlbumPc.Server;

namespace MonAlbumPc.App;

/// <summary>
/// Owns the core services for the whole life of the app, with what the mode needs: the network server when autonomous,
/// the look at the shared library in viewer mode.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    private MonAlbumServer? _server;

    private readonly FileLoggerProvider _log;
    private readonly WorkLog _workLog;

    public AppHost(string dataRoot)
    {
        DataRoot = dataRoot;
        Settings = AppSettings.Load(dataRoot);
        Core = CoreServices.Create(Settings.CatalogDirectory(dataRoot));
        // Start and end of each background task in server.log, in both modes.
        _log = new FileLoggerProvider(Path.Combine(Core.DataDirectory, "server.log"));
        _workLog = new WorkLog(Core.Work, _log.CreateLogger(WorkLog.Category));
        Library = Settings.IsViewer ? new LibraryViewer(Core, Settings.LibraryRoot!) : null;
        // Tags set by the NAS or another viewer of the same albums: taken from their journals.
        Core.Tags.StartPolling();
    }

    public string DataRoot { get; }
    public AppSettings Settings { get; }
    public CoreServices Core { get; }

    /// <summary>The shared library in viewer mode; null when this PC receives the photos itself.</summary>
    public LibraryViewer? Library { get; }

    public bool IsViewer => Library is not null;

    /// <summary>The Android app served to phones, when shipped with this PC.</summary>
    public ApkFile? Apk { get; } = ApkLocator.Find();

    /// <summary>Why the server could not start (port taken…), or null when phones can reach the PC.</summary>
    public string? ServerError { get; private set; }

    public int ApiPort => _server?.ApiPort ?? ServerOptions.DefaultApiPort;
    public int DownloadPort => _server?.DownloadPort ?? ServerOptions.DefaultDownloadPort;

    public IPAddress? LanAddress => PairingLink.FindLanAddress();

    public async Task StartAsync()
    {
        if (IsViewer)
        {
            return; // nothing to listen to: the app starts the library once it can show what it finds
        }
        // Shooting dates and places (map): indexed in the background at startup and after each backup.
        Core.Sessions.Ended += _ => Core.MediaIndexer.RequestRefresh();
        Core.MediaIndexer.RequestRefresh();
        try
        {
            // Its warnings and errors also show in the Activité page.
            _server = new MonAlbumServer(Core, new ServerOptions { Apk = Apk }, new WorkBoardLoggerProvider(Core.Work, _log));
            await _server.StartAsync();
            ServerError = null;
            Core.Work.Info($"Prêt à recevoir les photos : API {_server.ApiPort}, téléchargement {_server.DownloadPort}");
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            ServerError = "Les téléphones ne peuvent pas joindre ce PC : le port réseau est déjà utilisé par une autre application.";
            Core.Work.Error($"{ServerError} ({e.Message})");
            await DisposeServerAsync();
        }
    }

    // Phones look the PC up by mDNS: announce it again once it is back from sleep.
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _server?.RestartAdvertising();
        }
    }

    public async ValueTask DisposeAsync()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Library?.Dispose();
        await DisposeServerAsync();
        _workLog.Dispose();
        Core.Dispose();
    }

    private async Task DisposeServerAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }
    }
}
