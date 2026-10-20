namespace MonAlbumPc.Nas;

/// <summary>Settings of the NAS server, from environment variables (docker compose).</summary>
public sealed class NasOptions
{
    public const int DefaultAdminPort = 48622;

    /// <summary>Catalog, certificate and identity (<c>MONALBUMPC_CONFIG</c>, default <c>/config</c>).</summary>
    public string ConfigDirectory { get; init; } = "/config";

    /// <summary>Parent of every album folder (<c>MONALBUMPC_PHOTOS</c>, default <c>/photos</c>).</summary>
    public string PhotosRoot { get; init; } = "/photos";

    /// <summary>HTTPS port of the web administration (<c>MONALBUMPC_ADMIN_PORT</c>).</summary>
    public int AdminPort { get; init; } = DefaultAdminPort;

    /// <summary>Name shown to phones (<c>MONALBUMPC_NAME</c>, default the host name).</summary>
    public string ServerName { get; init; } = Environment.MachineName;

    /// <summary>Initial admin password (<c>MONALBUMPC_ADMIN_PASSWORD</c>); otherwise asked at first visit.</summary>
    public string? InitialAdminPassword { get; init; }

    /// <summary>Folder holding <c>monalbumpc.apk</c> and its <c>.json</c> (<c>MONALBUMPC_APK_DIR</c>).</summary>
    public string ApkDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "android");

    /// <summary>Deleted photos go here rather than being erased.</summary>
    public string TrashDirectory => Path.Combine(PhotosRoot, ".corbeille");

    public static NasOptions FromEnvironment()
    {
        static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
        var defaults = new NasOptions();
        return new NasOptions
        {
            ConfigDirectory = Env("MONALBUMPC_CONFIG") ?? defaults.ConfigDirectory,
            PhotosRoot = Env("MONALBUMPC_PHOTOS") ?? defaults.PhotosRoot,
            AdminPort = int.TryParse(Env("MONALBUMPC_ADMIN_PORT"), out var port) ? port : defaults.AdminPort,
            ServerName = Env("MONALBUMPC_NAME") ?? defaults.ServerName,
            InitialAdminPassword = Env("MONALBUMPC_ADMIN_PASSWORD"),
            ApkDirectory = Env("MONALBUMPC_APK_DIR") ?? defaults.ApkDirectory,
        };
    }

    /// <summary>True when <paramref name="path"/> is <see cref="PhotosRoot"/> or inside it.</summary>
    public bool IsUnderPhotos(string path)
    {
        var root = Path.GetFullPath(PhotosRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
