namespace MonAlbumPc.Core.Model;

/// <summary>An Android phone paired with one album of this PC.</summary>
public sealed record Device(
    Guid Id,
    Guid AlbumId,
    string Name,
    string Model,
    byte[] SecretHash,
    string? AppVersion,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt)
{
    public bool IsActive => RevokedAt is null;
}
