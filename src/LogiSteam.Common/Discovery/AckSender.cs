using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LogiSteam.Common.Discovery;

/// <summary>Runs on the Game PC. Sends a one-shot unicast ack back to a Wheel PC that beaconed it.</summary>
public static class AckSender
{
    public static async Task SendAsync(IPAddress wheelPcAddress, AckMessage message, CancellationToken ct = default)
    {
        try
        {
            using var client = new UdpClient();
            var payload = Encoding.UTF8.GetBytes(message.ToJson());
            var endpoint = new IPEndPoint(wheelPcAddress, BeaconProtocol.AckPort);
            await client.SendAsync(payload, payload.Length, endpoint).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // Best-effort; a transient network hiccup shouldn't matter, the next beacon cycle will retry.
        }
    }
}
