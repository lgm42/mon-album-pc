namespace MonAlbumPc.Core.Import;

/// <summary>The album folder cannot be reached (external disk unplugged, NAS off…). The phone retries later.</summary>
public sealed class DestinationUnavailableException(string root)
    : Exception($"Le dossier de destination {root} est introuvable.")
{
    public string Root { get; } = root;
}

/// <summary>The received bytes do not match the announced SHA-256. The upload starts over.</summary>
public sealed class HashMismatchException(string expected, string actual)
    : Exception($"Le fichier reçu ne correspond pas à son empreinte (attendu {expected}, reçu {actual}).");

/// <summary>No space left on the destination disk.</summary>
public sealed class DiskFullException(string root, Exception inner)
    : Exception($"Le disque de {root} est plein.", inner);
