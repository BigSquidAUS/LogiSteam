namespace LogiSteam.Common.Discovery;

public static class BeaconProtocol
{
    /// <summary>UDP port used for the LAN discovery beacon (Wheel PC -> Game PC).</summary>
    public const int Port = 35893;

    /// <summary>UDP port used for the return ack (Game PC -> Wheel PC), confirming the beacon was seen.</summary>
    public const int AckPort = 35894;

    /// <summary>How often the Wheel PC broadcasts its beacon while the wheel is shared.</summary>
    public static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the Game PC waits without a beacon before considering a Wheel PC gone. Set well
    /// above the G29's ~10 second autocentering calibration on connect (confirmed behavior), so a
    /// single calibration cycle can't itself look like the Wheel PC disappearing and trigger a
    /// detach that immediately causes another calibration on reattach.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(20);

    /// <summary>How long the Wheel PC waits without an ack before considering no Game PC is watching.</summary>
    public static readonly TimeSpan AckStaleAfter = TimeSpan.FromSeconds(20);
}
