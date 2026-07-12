using System.Net;

namespace LogiSteam.Common.Discovery;

/// <summary>A Wheel PC currently visible on the LAN, as tracked by <see cref="BeaconListener"/>.</summary>
public sealed record DiscoveredWheelPc(IPAddress Address, BeaconMessage Beacon, DateTime LastSeenUtc);
