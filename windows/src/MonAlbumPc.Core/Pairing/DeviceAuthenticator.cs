using System.Security.Cryptography;
using MonAlbumPc.Core.Model;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core.Pairing;

/// <summary>Checks the <c>Authorization: Bearer {deviceId}.{secret}</c> header sent by paired phones.</summary>
public sealed class DeviceAuthenticator(DeviceStore devices)
{
    public Device? Authenticate(string? authorization)
    {
        const string scheme = "Bearer ";
        if (authorization is null || !authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var credentials = authorization[scheme.Length..].Trim();
        var dot = credentials.IndexOf('.');
        if (dot <= 0 || !Guid.TryParse(credentials[..dot], out var deviceId))
        {
            return null;
        }
        var device = devices.Get(deviceId);
        if (device is null || !device.IsActive)
        {
            return null;
        }
        var hash = PairingService.HashSecret(credentials[(dot + 1)..]);
        return CryptographicOperations.FixedTimeEquals(hash, device.SecretHash) ? device : null;
    }
}
