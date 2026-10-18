using System.Net;

namespace MonAlbumPc.Server.Tests;

public sealed class DownloadPageTests
{
    [Fact]
    public async Task Qr_code_link_opens_a_page_that_launches_the_app_or_downloads_it()
    {
        await using var fixture = await ServerFixture.StartAsync();
        var session = fixture.Core.Pairing.Start(fixture.Album.Id);
        var link = PairingLink.Build(IPAddress.Loopback, fixture.Server.DownloadPort, fixture.Server.ApiPort, session,
            fixture.Core.CertificateFingerprint, fixture.Core.PcName);
        Assert.Contains($"t={session.Token}", link);
        Assert.Contains($"&p={fixture.Server.ApiPort}", link);

        using var http = new HttpClient();
        var html = await http.GetStringAsync(link);

        Assert.Contains("Sauvegardez vos photos sur PC-SALON", html);
        Assert.Contains($"intent://pair?t={session.Token}", html);
        Assert.Contains("package=io.github.lgm42.monalbumpc", html);
        Assert.Contains("pas encore disponible sur ce PC", html);
    }

    [Fact]
    public void Lan_address_is_an_ipv4_address()
    {
        var address = PairingLink.FindLanAddress();
        if (address is not null)
        {
            Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, address.AddressFamily);
            Assert.False(IPAddress.IsLoopback(address));
        }
    }
}
