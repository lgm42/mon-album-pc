using System.Net;
using System.Text.Json;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Library;
using MonAlbumPc.Server;

namespace MonAlbumPc.Nas;

/// <summary>Runs the phone-facing server (protocol API, download page, mDNS) for the life of the container.</summary>
public sealed class NasHost(NasOptions options, ILogger<NasHost> logger, ILoggerFactory loggers) : IHostedService, IAsyncDisposable
{
    private MonAlbumServer? _server;

    public CoreServices Core { get; } = CreateCore(options);

    /// <summary>The Android app shipped in the image, if any.</summary>
    public ApkFile? Apk { get; } = FindApk(options.ApkDirectory);

    /// <summary>Why phones cannot reach the server, or null when they can.</summary>
    public string? ServerError { get; private set; }

    public int ApiPort => _server?.ApiPort ?? ServerOptions.DefaultApiPort;
    public int DownloadPort => _server?.DownloadPort ?? ServerOptions.DefaultDownloadPort;

    public IPAddress? LanAddress => PairingLink.FindLanAddress();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.PhotosRoot);
        // Shooting places for a future map: indexed in the background, and again after each backup.
        Core.Sessions.Ended += _ => Core.MediaIndexer.RequestRefresh();
        Core.MediaIndexer.RequestRefresh();
        // What the Windows viewers read: <photos>/.monalbumpc.
        var publisher = new LibraryPublisher(Core, options.PhotosRoot);
        publisher.Failed += (what, e) => logger.LogError(e, "Écriture impossible dans la bibliothèque partagée ({What})", what);
        publisher.Start();
        try
        {
            // Its own web host: without the provider, its errors would never reach the container logs.
            _server = new MonAlbumServer(Core, new ServerOptions { Apk = Apk }, new ForwardingLoggerProvider(loggers));
            await _server.StartAsync(cancellationToken);
            logger.LogInformation("Serveur prêt : API {Api}, téléchargement {Download}, administration {Admin}",
                _server.ApiPort, _server.DownloadPort, options.AdminPort);
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
        {
            ServerError = $"Les téléphones ne peuvent pas joindre le serveur : {e.Message}";
            logger.LogError(e, "Le serveur des téléphones n'a pas pu démarrer");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
        {
            await _server.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
        Core.Dispose();
    }

    private static CoreServices CreateCore(NasOptions options)
    {
        var core = CoreServices.Create(options.ConfigDirectory);
        core.PcName = options.ServerName;
        return core;
    }

    private static ApkFile? FindApk(string directory)
    {
        var apk = Path.Combine(directory, "monalbumpc.apk");
        var manifest = apk + ".json";
        if (!File.Exists(apk) || !File.Exists(manifest))
        {
            return null;
        }
        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        return new ApkFile(apk, json.RootElement.GetProperty("versionCode").GetInt32(),
            json.RootElement.GetProperty("versionName").GetString() ?? "?");
    }
}
