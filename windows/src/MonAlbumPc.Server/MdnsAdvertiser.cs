using Makaretu.Dns;
using MonAlbumPc.Core;

namespace MonAlbumPc.Server;

/// <summary>
/// Announces the PC on the local network as <c>_monalbumpc._tcp</c> (docs/protocol.md §2), so that phones find
/// it by its id even when its IP address changes, and list it when pairing with the 6-digit code.
/// </summary>
public sealed class MdnsAdvertiser : IDisposable
{
    public const string ServiceType = "_monalbumpc._tcp";

    private readonly ServiceDiscovery _discovery;
    private readonly ServiceProfile _profile;

    public MdnsAdvertiser(CoreServices core, int apiPort)
    {
        _profile = new ServiceProfile(core.PcName, ServiceType, (ushort)apiPort);
        _profile.AddProperty("v", ApiEndpoints.Protocol.ToString());
        _profile.AddProperty("id", core.Identity.PcId.ToString());
        _profile.AddProperty("n", core.PcName);
        _discovery = new ServiceDiscovery();
        _discovery.Advertise(_profile);
    }

    public void Dispose()
    {
        try
        {
            _discovery.Unadvertise(_profile);
        }
        finally
        {
            _discovery.Dispose();
        }
    }
}
