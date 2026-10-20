using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MonAlbumPc.Nas;

/// <summary>The single administrator password of the web interface, stored as a salted PBKDF2 hash.</summary>
public sealed class AdminPassword(string configDirectory)
{
    private const int Iterations = 210_000;
    private const int MinimumLength = 8;

    private readonly string _path = Path.Combine(configDirectory, "admin.json");

    public bool IsSet => File.Exists(_path);

    /// <summary>Sets the password. Returns an error message when it is too weak.</summary>
    public string? Set(string password)
    {
        if (password.Length < MinimumLength)
        {
            return $"Le mot de passe doit faire au moins {MinimumLength} caractères.";
        }
        var salt = RandomNumberGenerator.GetBytes(16);
        var stored = new Stored(Convert.ToBase64String(salt), Convert.ToBase64String(Hash(password, salt)), Iterations,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(stored));
        return null;
    }

    public bool Verify(string password)
    {
        if (!IsSet)
        {
            return false;
        }
        var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path))!;
        var expected = Convert.FromBase64String(stored.Hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(stored.Salt),
            stored.Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>Changes with every new password; sign-in cookies carry it, so older ones stop working.</summary>
    public string? Stamp => IsSet ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path))!.Stamp ?? "" : null;

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);

    private sealed record Stored(string Salt, string Hash, int Iterations, string? Stamp = null);
}
