using System.Net;
using Microsoft.Win32;
using MonAlbumPc.Core;
using MonAlbumPc.Server;

namespace MonAlbumPc.App;

/// <summary>Owns the core services and the network server for the whole life of the app.</summary>
public sealed class AppHost : IAsyncDisposable
{
    private MonAlbumServer? _server;

    public AppHost(string dataDirectory)
    {
        Core = CoreServices.Create(dataDirectory);
    }

    public CoreServices Core { get; }

    /// <summary>The Android app served to phones, when shipped with this PC.</summary>
    public ApkFile? Apk { get; } = ApkLocator.Find();

    /// <summary>Why the server could not start (port taken…), or null when phones can reach the PC.</summary>
    public string? ServerError { get; private set; }

    public int ApiPort => _server?.ApiPort ?? ServerOptions.DefaultApiPort;
    public int DownloadPort => _server?.DownloadPort ?? ServerOptions.DefaultDownloadPort;

    public IPAddress? LanAddress => PairingLink.FindLanAddress();

    public async Task StartAsync()
    {
        // Shooting dates and places (map): indexed in the background at startup and after each backup.
        Core.Sessions.Ended += _ => Core.MediaIndexer.RequestRefresh();
        Core.MediaIndexer.RequestRefresh();
        try
        {
            _server = new MonAlbumServer(Core, new ServerOptions { Apk = Apk });
            await _server.StartAsync();
            ServerError = null;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            ServerError = "Les téléphones ne peuvent pas joindre ce PC : le port réseau est déjà utilisé par une autre application.";
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
        await DisposeServerAsync();
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
