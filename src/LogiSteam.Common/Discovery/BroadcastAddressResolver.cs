using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LogiSteam.Common.Discovery;

/// <summary>
/// Computes subnet-directed broadcast addresses (e.g. 192.168.1.255 for 192.168.1.50/24) for
/// every active IPv4 interface, so <see cref="BeaconBroadcaster"/> can send on each of them
/// directly instead of relying solely on the OS routing table's single choice of outbound NIC for
/// a global 255.255.255.255 send -- a VPN connecting, a Wi-Fi network change, or a new virtual
/// adapter appearing after the app is already running can silently redirect or blackhole that
/// single send with no error surfaced anywhere.
/// </summary>
public static class BroadcastAddressResolver
{
    /// <summary>Pure: computes the subnet-directed broadcast address for one (address, mask) pair.</summary>
    public static IPAddress GetSubnetBroadcast(IPAddress address, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        var broadcastBytes = new byte[addressBytes.Length];
        for (var i = 0; i < addressBytes.Length; i++)
        {
            broadcastBytes[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
        }

        return new IPAddress(broadcastBytes);
    }

    /// <summary>Pure: computes every subnet broadcast address from an already-gathered list of (address, mask) pairs.</summary>
    public static IReadOnlyList<IPAddress> GetSubnetBroadcastAddresses(IEnumerable<(IPAddress Address, IPAddress Mask)> interfaces) =>
        interfaces.Select(i => GetSubnetBroadcast(i.Address, i.Mask)).ToList();

    /// <summary>
    /// Real I/O: enumerates this machine's currently-active IPv4 interfaces. Re-called on every
    /// broadcast loop iteration (not cached) since interfaces can appear/disappear while the app
    /// is already running.
    /// </summary>
    public static IReadOnlyList<IPAddress> GetActiveSubnetBroadcastAddresses()
    {
        var interfaces =
            from nic in NetworkInterface.GetAllNetworkInterfaces()
            where nic.OperationalStatus == OperationalStatus.Up
                && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
            from unicast in nic.GetIPProperties().UnicastAddresses
            where unicast.Address.AddressFamily == AddressFamily.InterNetwork
            select (unicast.Address, unicast.IPv4Mask);

        return GetSubnetBroadcastAddresses(interfaces);
    }
}
