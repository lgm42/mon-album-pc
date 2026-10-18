namespace MonAlbumPc.Server;

public sealed class ServerOptions
{
    public const int DefaultApiPort = 48620;
    public const int DefaultDownloadPort = 48621;

    /// <summary>HTTPS API port. 0 picks a free port (tests).</summary>
    public int ApiPort { get; init; } = DefaultApiPort;

    /// <summary>Plain HTTP port serving the download page and the APK. 0 picks a free port (tests).</summary>
    public int DownloadPort { get; init; } = DefaultDownloadPort;

    /// <summary>Announce the PC on the network with mDNS (off in tests).</summary>
    public bool Advertise { get; init; } = true;

    /// <summary>The Android app shipped with this PC, if any.</summary>
    public ApkFile? Apk { get; init; }
}

/// <summary>An APK file and its version, served to phones for installs and updates.</summary>
public sealed record ApkFile(string Path, int VersionCode, string VersionName);
