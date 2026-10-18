using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MonAlbumPc.Core.Pairing;

/// <summary>
/// Self-signed TLS certificate of this PC. Phones pin its SHA-256 when pairing, so it must never change
/// once created.
/// </summary>
public static class ServerCertificate
{
    public static X509Certificate2 LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            return Load(Unprotect(File.ReadAllBytes(path)));
        }
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Mon Album PC", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var now = DateTimeOffset.UtcNow;
        using var created = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(30));
        var pfx = created.Export(X509ContentType.Pfx);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Protect(pfx));
        return Load(pfx);
    }

    /// <summary>SHA-256 of the DER certificate, base64url: the value carried by the QR code and pinned by phones.</summary>
    public static string Fingerprint(X509Certificate2 certificate) =>
        Convert.ToBase64String(SHA256.HashData(certificate.RawData)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Round-tripping through PFX gives a persisted key that SChannel (Kestrel on Windows) can use.
    private static X509Certificate2 Load(byte[] pfx) => X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);

    private static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine) : data;

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows() ? ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine) : data;
}
