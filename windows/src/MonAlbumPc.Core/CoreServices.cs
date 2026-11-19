using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using MonAlbumPc.Core.Activity;
using MonAlbumPc.Core.Import;
using MonAlbumPc.Core.Media;
using MonAlbumPc.Core.Pairing;
using MonAlbumPc.Core.Storage;
using MonAlbumPc.Core.Tags;

namespace MonAlbumPc.Core;

/// <summary>Identity of this PC on the network, created once.</summary>
public sealed record PcIdentity(Guid PcId);

/// <summary>Composition root shared by the server and the Windows app.</summary>
public sealed class CoreServices : IDisposable
{
    private CoreServices(string dataDirectory, TimeProvider clock, WorkBoard? work)
    {
        Directory.CreateDirectory(dataDirectory);
        DataDirectory = dataDirectory;
        Clock = clock;
        Work = work ?? new WorkBoard(clock);
        Identity = LoadIdentity(Path.Combine(dataDirectory, "identity.json"));
        Database = new Database(Path.Combine(dataDirectory, "catalog.db"));
        Albums = new AlbumStore(Database);
        Devices = new DeviceStore(Database);
        Devices.MergeSameNames();
        History = new ImportHistory(Database);
        Journals = new AlbumJournals(Albums, Devices, Identity.PcId, clock);
        History.Journals = Journals;
        var tagCatalog = new TagCatalog(Database);
        var tagJournals = new TagJournals(Albums, tagCatalog, Identity.PcId);
        Media = new MediaIndex(Database, Journals, History, tagJournals);
        MediaIndexer = new MediaIndexer(Albums, Media, Work);
        Tags = new TagService(Albums, History, tagCatalog, tagJournals, Work);
        Import = new ImportService(Albums, History, clock, Media);
        Pairing = new PairingService(Albums, Devices, clock);
        Authenticator = new DeviceAuthenticator(Devices);
        Uploads = new UploadStore(clock);
        SessionHistory = new SessionStore(Database);
        Sessions = new SessionTracker(SessionHistory, clock);
        SessionReport.Attach(this);
        var certificate = Path.Combine(dataDirectory, "server-certificate.dat");
        var created = !File.Exists(certificate);
        Certificate = ServerCertificate.LoadOrCreate(certificate);
        if (created)
        {
            Work.Info($"Certificat du serveur créé (empreinte {CertificateFingerprint[..8]}…)");
        }
    }

    /// <summary>Machine-wide data folder: one instance of the app serves every album of the PC.</summary>
    public static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MonAlbumPC");

    public string DataDirectory { get; }
    public TimeProvider Clock { get; }

    /// <summary>What runs in the background right now, and what happened recently.</summary>
    public WorkBoard Work { get; }
    public PcIdentity Identity { get; }
    public string PcName { get; set; } = Environment.MachineName;
    public Database Database { get; }
    public AlbumStore Albums { get; }
    public DeviceStore Devices { get; }
    public ImportHistory History { get; }
    public AlbumJournals Journals { get; }
    public ImportService Import { get; }

    /// <summary>Shooting date and place of every file of the album folders.</summary>
    public MediaIndex Media { get; }

    /// <summary>Background refresh of <see cref="Media"/>, started by the hosts (app, NAS).</summary>
    public MediaIndexer MediaIndexer { get; }

    /// <summary>User tags of the photos: in the files, indexed here, shared with the other servers.</summary>
    public TagService Tags { get; }
    public PairingService Pairing { get; }
    public DeviceAuthenticator Authenticator { get; }
    public UploadStore Uploads { get; }
    public SessionStore SessionHistory { get; }
    public SessionTracker Sessions { get; }
    public TransferLocks Locks { get; } = new();
    public X509Certificate2 Certificate { get; }
    public string CertificateFingerprint => ServerCertificate.Fingerprint(Certificate);

    /// <param name="work">The board of the host when it already has one (to catch its logs from the start).</param>
    public static CoreServices Create(string dataDirectory, TimeProvider? clock = null, WorkBoard? work = null) =>
        new(dataDirectory, clock ?? TimeProvider.System, work);

    public void Dispose()
    {
        Tags.StopPolling();
        MediaIndexer.Dispose();
        Certificate.Dispose();
        Work.Dispose();
    }

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
