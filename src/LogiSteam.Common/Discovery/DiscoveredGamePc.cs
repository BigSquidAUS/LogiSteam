using System.Net;

namespace LogiSteam.Common.Discovery;

/// <summary>A Game PC currently acking this Wheel PC's beacon, as tracked by <see cref="AckListener"/>.</summary>
public sealed record DiscoveredGamePc(IPAddress Address, AckMessage Ack, DateTime LastSeenUtc);
