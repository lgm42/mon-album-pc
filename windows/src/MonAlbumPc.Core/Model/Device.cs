namespace MonAlbumPc.Core.Model;

/// <summary>
/// An Android phone paired with one album of this PC. <see cref="HardwareId"/> (the Android ID of the app) recognizes
/// the same phone when it pairs again, so it keeps its history instead of appearing twice.
/// </summary>
public sealed record Device(
    Guid Id,
    Guid AlbumId,
    string Name,
    string Model,
    byte[] SecretHash,
    string? AppVersion,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    string? HardwareId = null)
{
    public bool IsActive => RevokedAt is null;
}
