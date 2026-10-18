using System.Net.Http.Headers;
using System.Net.Http.Json;
using MonAlbumPc.Core;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Tests;

namespace MonAlbumPc.Server.Tests;

/// <summary>A running server on free ports, with one album, and clients that pin its certificate.</summary>
public sealed class ServerFixture : IAsyncDisposable
{
    private readonly TempDirectory _temp = new();

    private ServerFixture()
    {
        Core = CoreServices.Create(_temp.Combine("data"));
        Core.PcName = "PC-SALON";
        Album = new Album(Guid.NewGuid(), "Marie", _temp.Combine("Photos", "Marie"), "#3D4FD1", DateTimeOffset.Now);
        Directory.CreateDirectory(Album.DestinationRoot);
        Core.Albums.Add(Album);
    }

    public CoreServices Core { get; }
    public Album Album { get; }
    public MonAlbumServer Server { get; private set; } = null!;
    public TempDirectory Temp => _temp;

    public static async Task<ServerFixture> StartAsync(ServerOptions? options = null)
    {
        var fixture = new ServerFixture();
        fixture.Server = new MonAlbumServer(fixture.Core, options ?? new ServerOptions { ApiPort = 0, DownloadPort = 0, Advertise = false });
        await fixture.Server.StartAsync();
        return fixture;
    }

    /// <summary>HTTPS client accepting only this PC's certificate, like the phone after pairing.</summary>
    public HttpClient Api(string? deviceId = null, string? secret = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && Core.CertificateFingerprint == MonAlbumPc.Core.Pairing.ServerCertificate.Fingerprint(certificate),
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{Server.ApiPort}/api/v1/") };
        if (deviceId is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", $"{deviceId}.{secret}");
        }
        return client;
    }

    public HttpClient Download() => new() { BaseAddress = new Uri($"http://127.0.0.1:{Server.DownloadPort}/") };

    /// <summary>Pairs a phone through the API and returns a client authenticated as it.</summary>
    public async Task<(HttpClient Client, PairResponse Pairing)> PairAsync()
    {
        var session = Core.Pairing.Start(Album.Id);
        using var anonymous = Api();
        var response = await anonymous.PostAsJsonAsync("pair", new PairRequest(session.Token, null,
            new PairDevice("A35 de Marie", "Samsung Galaxy A35", "15", "1.0.0", 1)));
        response.EnsureSuccessStatusCode();
        var pairing = (await response.Content.ReadFromJsonAsync<PairResponse>())!;
        return (Api(pairing.DeviceId.ToString(), pairing.Secret), pairing);
    }

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
        Core.Dispose();
        _temp.Dispose();
    }
}
