using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Pairing;
using MonAlbumPc.Core.Storage;

namespace MonAlbumPc.Core;

/// <summary>Identity of this PC on the network, created once.</summary>
public sealed record PcIdentity(Guid PcId);

/// <summary>Composition root shared by the server and the Windows app.</summary>
public sealed class CoreServices : IDisposable
{
    private CoreServices(string dataDirectory, TimeProvider clock)
    {
        Directory.CreateDirectory(dataDirectory);
        DataDirectory = dataDirectory;
        Clock = clock;
        Identity = LoadIdentity(Path.Combine(dataDirectory, "identity.json"));
        Database = new Database(Path.Combine(dataDirectory, "catalog.db"));
        Albums = new AlbumStore(Database);
        Devices = new DeviceStore(Database);
        History = new ImportHistory(Database);
        Journals = new AlbumJournals(Albums, Devices, Identity.PcId, clock);
        History.Journals = Journals;
        Import = new ImportService(Albums, History, clock);
        Pairing = new PairingService(Albums, Devices, clock);
        Authenticator = new DeviceAuthenticator(Devices);
        Uploads = new UploadStore(clock);
        SessionHistory = new SessionStore(Database);
        Sessions = new SessionTracker(SessionHistory, clock);
        Certificate = ServerCertificate.LoadOrCreate(Path.Combine(dataDirectory, "server-certificate.dat"));
    }

    /// <summary>Machine-wide data folder: one instance of the app serves every album of the PC.</summary>
    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MonAlbumPC");

    public string DataDirectory { get; }
    public TimeProvider Clock { get; }
    public PcIdentity Identity { get; }
    public string PcName { get; set; } = Environment.MachineName;
    public Database Database { get; }
    public AlbumStore Albums { get; }
    public DeviceStore Devices { get; }
    public ImportHistory History { get; }
    public AlbumJournals Journals { get; }
    public ImportService Import { get; }
    public PairingService Pairing { get; }
    public DeviceAuthenticator Authenticator { get; }
    public UploadStore Uploads { get; }
    public SessionStore SessionHistory { get; }
    public SessionTracker Sessions { get; }
    public TransferLocks Locks { get; } = new();
    public X509Certificate2 Certificate { get; }
    public string CertificateFingerprint => ServerCertificate.Fingerprint(Certificate);

    public static CoreServices Create(string dataDirectory, TimeProvider? clock = null) =>
        new(dataDirectory, clock ?? TimeProvider.System);

    public void Dispose() => Certificate.Dispose();

    private static PcIdentity LoadIdentity(string path)
    {
        if (File.Exists(path))
        {
            return JsonSerializer.Deserialize<PcIdentity>(File.ReadAllText(path))!;
        }
        var identity = new PcIdentity(Guid.NewGuid());
        File.WriteAllText(path, JsonSerializer.Serialize(identity));
        return identity;
    }
}
