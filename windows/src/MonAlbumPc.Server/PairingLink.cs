using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MonAlbumPc.Core.Pairing;

namespace MonAlbumPc.Server;

/// <summary>The URL carried by the pairing QR code (docs/protocol.md §3.1).</summary>
public static class PairingLink
{
    public static string Build(IPAddress address, int downloadPort, int apiPort, PairingSession session, string fingerprint, string pcName)
    {
        var query = $"t={session.Token}&f={fingerprint}&h={address}&n={Uri.EscapeDataString(pcName)}";
        if (apiPort != ServerOptions.DefaultApiPort)
        {
            query += $"&p={apiPort}";
        }
        return $"http://{address}:{downloadPort}/p?{query}";
    }

    /// <summary>
    /// The address phones on the home network can reach: an IPv4 address of an active, non-virtual interface that
    /// has a default gateway (skips VPNs, Hyper-V and Docker adapters when possible).
    /// </summary>
    public static IPAddress? FindLanAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Select(n => (Interface: n, Properties: n.GetIPProperties()))
            .Select(x => (
                x.Interface,
                Address: x.Properties.UnicastAddresses
                    .Select(a => a.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)),
                HasGateway: x.Properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                                                   && !g.Address.Equals(IPAddress.Any))))
            .Where(x => x.Address is not null)
            .OrderByDescending(x => x.HasGateway)
            .ThenByDescending(x => IsPhysical(x.Interface))
            .ThenByDescending(x => x.Interface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        return candidates.Select(x => x.Address).FirstOrDefault();
    }

    private static bool IsPhysical(NetworkInterface n)
    {
        var description = n.Description.ToLowerInvariant();
        return !(description.Contains("virtual") || description.Contains("hyper-v") || description.Contains("vpn")
                 || description.Contains("wireguard") || description.Contains("docker") || description.Contains("tap-"));
    }
}
