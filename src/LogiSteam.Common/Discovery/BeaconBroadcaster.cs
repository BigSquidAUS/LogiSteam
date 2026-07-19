using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace LogiSteam.Common.Discovery;

/// <summary>
/// Runs on the Wheel PC. Periodically broadcasts a <see cref="BeaconMessage"/> on the LAN
/// so LogiSteam.GameSide can find this machine without any manual configuration.
/// </summary>
public sealed class BeaconBroadcaster : IDisposable
{
    private readonly Func<BeaconMessage?> _messageFactory;
    private UdpClient _client;
    private CancellationTokenSource _cts = new();
    private Task? _loop;

    public BeaconBroadcaster(Func<BeaconMessage?> messageFactory)
    {
        _messageFactory = messageFactory;
        _client = new UdpClient { EnableBroadcast = true };
    }

    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var globalBroadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, BeaconProtocol.Port);

        while (!ct.IsCancellationRequested)
        {
            var message = _messageFactory();
            if (message is not null)
            {
                var payload = Encoding.UTF8.GetBytes(message.ToJson());

                // Send the global broadcast (works on most single-NIC setups) plus every active
                // interface's own subnet-directed broadcast (re-resolved every loop iteration,
                // since interfaces can appear/disappear -- VPN connect, Wi-Fi switch, docking --
                // while the app is already running). Each send is independent so one bad/blocked
                // interface can't prevent the others from getting through.
                await SendBestEffortAsync(payload, globalBroadcastEndpoint).ConfigureAwait(false);
                foreach (var address in TryGetActiveSubnetBroadcastAddresses())
                {
                    await SendBestEffortAsync(payload, new IPEndPoint(address, BeaconProtocol.Port)).ConfigureAwait(false);
                }
            }

            try
            {
                await Task.Delay(BeaconProtocol.BroadcastInterval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private static IReadOnlyList<IPAddress> TryGetActiveSubnetBroadcastAddresses()
    {
        try
        {
            return BroadcastAddressResolver.GetActiveSubnetBroadcastAddresses();
        }
        catch (NetworkInformationException)
        {
            // Best-effort: the global broadcast send above still covers the common single-NIC case.
            return [];
        }
    }

    private async Task SendBestEffortAsync(byte[] payload, IPEndPoint endpoint)
    {
        try
        {
            await _client.SendAsync(payload, payload.Length, endpoint).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            // Best-effort broadcast; a transient network hiccup on one interface shouldn't kill the loop.
        }
    }

    /// <summary>
    /// Tears down and recreates the underlying socket and broadcast loop in place -- recovers
    /// from a system sleep/resume cycle, where the previously created UdpClient can be left
    /// silently unable to send by a NIC that got power-cycled during suspend even though the
    /// process itself survives untouched.
    /// </summary>
    public void Restart()
    {
        TearDown();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _client = new UdpClient { EnableBroadcast = true };
        _loop = null;
        Start();
    }

    public void Dispose()
    {
        TearDown();
        _cts.Dispose();
    }

    private void TearDown()
    {
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Expected on cancellation.
        }

        _client.Dispose();
    }
}
