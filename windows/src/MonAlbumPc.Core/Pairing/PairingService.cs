using System.Security.Cryptography;
using System.Text;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Pairing;

/// <summary>What the phone tells about itself when pairing.</summary>
public sealed record DeviceInfo(string Name, string Model, string? AndroidVersion, string? AppVersion, int? AppVersionCode,
    string? HardwareId = null);

/// <summary>A pairing offer shown on the PC: QR code token and 6-digit code, valid for a few minutes.</summary>
public sealed record PairingSession(Guid AlbumId, string Token, string Code, DateTimeOffset ExpiresAt);

public sealed record PairingResult(Device Device, string Secret, Album Album);

public enum PairingError { Invalid, Expired }

public sealed class PairingException(PairingError error, string message) : Exception(message)
{
    public PairingError Error { get; } = error;
}

/// <summary>Pairs phones with an album, one offer at a time.</summary>
public sealed class PairingService(AlbumStore albums, DeviceStore devices, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public const int MaxCodeAttempts = 5;

    private readonly Lock _lock = new();
    private PairingSession? _session;
    private int _failedCodeAttempts;

    /// <summary>Raised after a phone has been paired (the wizard moves to its success step).</summary>
    public event Action<PairingResult>? Paired;

    /// <summary>Raised when the current offer becomes unusable (too many wrong codes).</summary>
    public event Action? SessionInvalidated;

    public PairingSession? Current
    {
        get
        {
            lock (_lock)
            {
                return _session is not null && _session.ExpiresAt > clock.GetUtcNow() ? _session : null;
            }
        }
    }

    /// <summary>Creates a new offer for <paramref name="albumId"/>, replacing any previous one.</summary>
    public PairingSession Start(Guid albumId)
    {
        _ = albums.Get(albumId) ?? throw new InvalidOperationException($"Album {albumId} inconnu.");
        var session = new PairingSession(
            albumId,
            Base32(RandomNumberGenerator.GetBytes(16)),
            RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"),
            clock.GetUtcNow() + Lifetime);
        lock (_lock)
        {
            _session = session;
            _failedCodeAttempts = 0;
        }
        return session;
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _session = null;
        }
    }

    public PairingResult PairWithToken(string token, DeviceInfo info) =>
        Pair(session => FixedTimeEquals(session.Token, token), countFailure: false, info);

    public PairingResult PairWithCode(string code, DeviceInfo info) =>
        Pair(session => FixedTimeEquals(session.Code, code.Trim()), countFailure: true, info);

    private PairingResult Pair(Func<PairingSession, bool> matches, bool countFailure, DeviceInfo info)
    {
        PairingSession? session = null;
        lock (_lock)
        {
            if (_session is null)
            {
                throw new PairingException(PairingError.Expired, "Aucune association en cours sur le PC.");
            }
            if (_session.ExpiresAt <= clock.GetUtcNow())
            {
                _session = null;
                throw new PairingException(PairingError.Expired, "Ce code a expiré.");
            }
            if (matches(_session))
            {
                session = _session;
            }
            else if (!countFailure || ++_failedCodeAttempts < MaxCodeAttempts)
            {
                throw new PairingException(PairingError.Invalid, "Ce code n'est pas valable.");
            }
            _session = null; // single use, or burnt after too many wrong codes
        }
        if (session is null)
        {
            SessionInvalidated?.Invoke();
            throw new PairingException(PairingError.Expired, "Trop d'essais : ce code a expiré.");
        }

        var album = albums.Get(session.AlbumId) ?? throw new PairingException(PairingError.Invalid, "L'album n'existe plus.");
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        var hardwareId = string.IsNullOrWhiteSpace(info.HardwareId) ? null : Truncate(info.HardwareId, 64);
        var known = hardwareId is null ? null : devices.FindByHardwareId(album.Id, hardwareId);
        Device device;
        if (known is not null)
        {
            // Same phone paired again (reinstalled app…): keep its name and history.
            device = known with
            {
                Model = Truncate(info.Model, 80), SecretHash = HashSecret(secret), AppVersion = info.AppVersion,
                PairedAt = clock.GetLocalNow(), LastSeenAt = clock.GetLocalNow(), RevokedAt = null, HardwareId = hardwareId,
            };
            devices.Repair(device);
        }
        else
        {
            device = new Device(
                Guid.NewGuid(), album.Id, Truncate(info.Name, 60), Truncate(info.Model, 80), HashSecret(secret),
                info.AppVersion, clock.GetLocalNow(), clock.GetLocalNow(), null, hardwareId);
            devices.Add(device);
        }

        var result = new PairingResult(device, secret, album);
        Paired?.Invoke(result);
        return result;
    }

    public static byte[] HashSecret(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
        {
            output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        }
        return output.ToString();
    }
}
