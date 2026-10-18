using System.Net;
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
        try
        {
            _server = new MonAlbumServer(Core, new ServerOptions { Apk = Apk });
            await _server.StartAsync();
            ServerError = null;
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            ServerError = "Les téléphones ne peuvent pas joindre ce PC : le port réseau est déjà utilisé par une autre application.";
            await DisposeServerAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
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
