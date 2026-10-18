using System.Security.Cryptography;

namespace MonAlbumPc.Core.Import;

public static class Hashing
{
    /// <summary>SHA-256 of a file as lowercase hex, the identifier used everywhere in the protocol.</summary>
    public static string Sha256File(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
