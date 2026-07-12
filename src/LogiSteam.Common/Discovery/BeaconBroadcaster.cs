using System.Net;
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
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _cts = new();
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
        var endpoint = new IPEndPoint(IPAddress.Broadcast, BeaconProtocol.Port);

        while (!ct.IsCancellationRequested)
        {
            var message = _messageFactory();
            if (message is not null)
            {
                var payload = Encoding.UTF8.GetBytes(message.ToJson());
                try
                {
                    await _client.SendAsync(payload, payload.Length, endpoint).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                    // Best-effort broadcast; a transient network hiccup shouldn't kill the loop.
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

    public void Dispose()
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
        _cts.Dispose();
    }
}
