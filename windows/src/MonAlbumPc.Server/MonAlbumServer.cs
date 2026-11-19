using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MonAlbumPc.Core;

namespace MonAlbumPc.Server;

/// <summary>
/// The PC side of the protocol: HTTPS API for paired phones and plain HTTP download page, both on all interfaces
/// so phones on the home Wi-Fi can reach them.
/// </summary>
public sealed class MonAlbumServer(CoreServices core, ServerOptions options, ILoggerProvider? logger = null) : IAsyncDisposable
{
    private WebApplication? _app;
    private MdnsAdvertiser? _mdns;
    private Timer? _idleSessions;
    private SessionLog? _sessionLog;
    private ILogger? _log;

    public int ApiPort { get; private set; }
    public int DownloadPort { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        if (logger is not null)
        {
            builder.Logging.AddProvider(logger);
        }
        builder.Services.AddSingleton(core);
        builder.Services.AddSingleton(options);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Limits.MaxRequestBodySize = null; // videos are several GB
            kestrel.AddServerHeader = false;
            kestrel.Listen(IPAddress.Any, options.ApiPort, listen => listen.UseHttps(core.Certificate));
            kestrel.Listen(IPAddress.Any, options.DownloadPort);
        });

        _app = builder.Build();
        var loggers = _app.Services.GetRequiredService<ILoggerFactory>();
        _sessionLog = new SessionLog(core, loggers.CreateLogger(SessionLog.Category));
        ApiEndpoints.Map(_app, core, options);
        DownloadEndpoints.Map(_app, core, options);
        await _app.StartAsync(cancellationToken);

        var ports = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.Select(a => new Uri(a.Replace("0.0.0.0", "localhost"))).ToList();
        ApiPort = ports.First(u => u.Scheme == "https").Port;
        DownloadPort = ports.First(u => u.Scheme == "http").Port;
        _log = loggers.CreateLogger("MonAlbumPc.Server");
        Advertise();
        // Albums imported before journals existed: write their journal next to the photos (may be a slow network folder).
        _ = Task.Run(() =>
        {
            using var job = core.Work.Start("Journaux des albums", "Écriture des journaux manquants", silent: true);
            core.Journals.ExportMissing(core.History);
        }, cancellationToken);
        _idleSessions = new Timer(_ => core.Sessions.CloseIdle(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>Announces the PC again, e.g. after it wakes up: its network stack may have been reset.</summary>
    public void RestartAdvertising()
    {
        if (!options.Advertise || _app is null)
        {
            return;
        }
        _mdns?.Dispose();
        _mdns = null;
        Advertise();
        if (_mdns is not null)
        {
            core.Work.Info("Annonce sur le réseau relancée après la mise en veille");
        }
    }

    // Without the announcement, paired phones still reach the server at its last known address: worth a warning, not a stop.
    private void Advertise()
    {
        if (!options.Advertise)
        {
            return;
        }
        try
        {
            _mdns = new MdnsAdvertiser(core, ApiPort);
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or InvalidOperationException or IOException)
        {
            _log?.LogWarning(e, "Annonce sur le réseau (mDNS) impossible : les téléphones ne retrouveront ce serveur que s'il garde la même adresse");
        }
    }

    public async Task StopAsync()
    {
        _idleSessions?.Dispose();
        _sessionLog?.Dispose();
        _mdns?.Dispose();
        _mdns = null;
        if (_app is not null)
        {
            await _app.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _idleSessions?.Dispose();
        _sessionLog?.Dispose();
        _mdns?.Dispose();
        _mdns = null;
        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
