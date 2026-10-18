using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Pairing;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Tests.Pairing;

public sealed class PairingServiceTests : IDisposable
{
    private static readonly DeviceInfo Phone = new("A35 de Marie", "Samsung Galaxy A35", "15", "1.0.0", 1);
    private readonly TempDirectory _temp = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly DeviceStore _devices;
    private readonly PairingService _pairing;
    private readonly DeviceAuthenticator _authenticator;
    private readonly Album _album;

    public PairingServiceTests()
    {
        var database = new Database(_temp.Combine("catalog.db"));
        var albums = new AlbumStore(database);
        _devices = new DeviceStore(database);
        _pairing = new PairingService(albums, _devices, _clock);
        _authenticator = new DeviceAuthenticator(_devices);
        _album = new Album(Guid.NewGuid(), "Marie", _temp.Path, "#3D4FD1", _clock.GetUtcNow());
        albums.Add(_album);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Session_has_a_base32_token_and_a_six_digit_code()
    {
        var session = _pairing.Start(_album.Id);

        Assert.Matches("^[A-Z2-7]{26}$", session.Token);
        Assert.Matches("^[0-9]{6}$", session.Code);
        Assert.Equal(_clock.GetUtcNow() + PairingService.Lifetime, session.ExpiresAt);
    }

    [Fact]
    public void Token_pairs_the_phone_with_the_album_and_its_secret_authenticates()
    {
        var session = _pairing.Start(_album.Id);
        PairingResult? raised = null;
        _pairing.Paired += r => raised = r;

        var result = _pairing.PairWithToken(session.Token, Phone);

        Assert.Same(result, raised);
        Assert.Equal(_album.Id, result.Device.AlbumId);
        Assert.Equal("A35 de Marie", Assert.Single(_devices.ListByAlbum(_album.Id)).Name);
        Assert.Equal(result.Device.Id, _authenticator.Authenticate($"Bearer {result.Device.Id}.{result.Secret}")?.Id);
        Assert.Null(_authenticator.Authenticate($"Bearer {result.Device.Id}.wrong"));
        Assert.Null(_authenticator.Authenticate(null));
        Assert.Null(_authenticator.Authenticate("Basic abc"));
    }

    [Fact]
    public void Offers_are_single_use()
    {
        var session = _pairing.Start(_album.Id);
        _pairing.PairWithToken(session.Token, Phone);

        var error = Assert.Throws<PairingException>(() => _pairing.PairWithToken(session.Token, Phone));
        Assert.Equal(PairingError.Expired, error.Error);
    }

    [Fact]
    public void Offers_expire_after_five_minutes()
    {
        var session = _pairing.Start(_album.Id);
        _clock.Advance(PairingService.Lifetime);

        Assert.Null(_pairing.Current);
        Assert.Equal(PairingError.Expired, Assert.Throws<PairingException>(() => _pairing.PairWithCode(session.Code, Phone)).Error);
    }

    [Fact]
    public void Code_is_burnt_after_five_wrong_attempts()
    {
        var session = _pairing.Start(_album.Id);
        var wrong = session.Code == "000000" ? "111111" : "000000";
        var invalidated = false;
        _pairing.SessionInvalidated += () => invalidated = true;

        for (var i = 1; i < PairingService.MaxCodeAttempts; i++)
        {
            Assert.Equal(PairingError.Invalid, Assert.Throws<PairingException>(() => _pairing.PairWithCode(wrong, Phone)).Error);
        }
        Assert.Equal(PairingError.Expired, Assert.Throws<PairingException>(() => _pairing.PairWithCode(wrong, Phone)).Error);
        Assert.True(invalidated);
        Assert.Throws<PairingException>(() => _pairing.PairWithCode(session.Code, Phone));
    }

    [Fact]
    public void Wrong_token_does_not_burn_the_offer()
    {
        var session = _pairing.Start(_album.Id);
        for (var i = 0; i < 10; i++)
        {
            Assert.Throws<PairingException>(() => _pairing.PairWithToken("NOPE", Phone));
        }
        Assert.NotNull(_pairing.PairWithToken(session.Token, Phone));
    }

    [Fact]
    public void Revoked_device_no_longer_authenticates()
    {
        var session = _pairing.Start(_album.Id);
        var result = _pairing.PairWithCode(session.Code, Phone);

        _devices.Revoke(result.Device.Id, _clock.GetUtcNow());

        Assert.Null(_authenticator.Authenticate($"Bearer {result.Device.Id}.{result.Secret}"));
    }

    [Fact]
    public void Certificate_is_created_once_and_keeps_its_fingerprint()
    {
        var path = _temp.Combine("server.pfx.dat");
        using var first = ServerCertificate.LoadOrCreate(path);
        using var second = ServerCertificate.LoadOrCreate(path);

        Assert.True(first.HasPrivateKey);
        Assert.Equal(ServerCertificate.Fingerprint(first), ServerCertificate.Fingerprint(second));
        Assert.Matches("^[A-Za-z0-9_-]{43}$", ServerCertificate.Fingerprint(first));
    }
}

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
