using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LogiSteam.Common.Discovery;

/// <summary>
/// Runs on the Game PC. Listens for <see cref="BeaconMessage"/> broadcasts and tracks which
/// Wheel PCs are currently visible on the LAN, so LogiSteam.GameSide never needs a manually
/// typed IP address or bus id.
/// </summary>
public sealed class BeaconListener : IDisposable
{
    private readonly ConcurrentDictionary<IPAddress, DiscoveredWheelPc> _seen = new();
    private CancellationTokenSource _cts = new();
    private UdpClient? _client;
    private Task? _receiveLoop;
    private Task? _sweepLoop;

    public event Action<DiscoveredWheelPc>? WheelPcDiscoveredOrUpdated;
    public event Action<IPAddress>? WheelPcLost;

    public IReadOnlyCollection<DiscoveredWheelPc> CurrentlyVisible => _seen.Values.ToArray();

    public void Start()
    {
        if (_client is not null)
        {
            return;
        }

        _client = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
        };
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, BeaconProtocol.Port));

        _receiveLoop = Task.Run(() => ReceiveAsync(_cts.Token));
        _sweepLoop = Task.Run(() => SweepStaleAsync(_cts.Token));
    }

    private async Task ReceiveAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _client!.ReceiveAsync(ct).ConfigureAwait(false);
                var json = Encoding.UTF8.GetString(result.Buffer);
                if (!BeaconMessage.TryParse(json, out var message) || message is null)
                {
                    continue;
                }

                var discovered = new DiscoveredWheelPc(result.RemoteEndPoint.Address, message, DateTime.UtcNow);
                _seen[discovered.Address] = discovered;
                WheelPcDiscoveredOrUpdated?.Invoke(discovered);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // Transient network error; keep listening.
            }
        }
    }

    private async Task SweepStaleAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(BeaconProtocol.BroadcastInterval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var cutoff = DateTime.UtcNow - BeaconProtocol.StaleAfter;
            foreach (var kvp in _seen)
            {
                if (kvp.Value.LastSeenUtc < cutoff && _seen.TryRemove(kvp.Key, out _))
                {
                    WheelPcLost?.Invoke(kvp.Key);
                }
            }
        }
    }

    /// <summary>
    /// Tears down and rebinds the underlying socket and receive/sweep loops in place, without
    /// disturbing event subscribers -- recovers from a system sleep/resume cycle, where the
    /// previously bound UdpClient can be left silently dead by a NIC that got power-cycled during
    /// suspend even though the process itself, and everything subscribed to this listener,
    /// survives untouched.
    /// </summary>
    public void Restart()
    {
        TearDown();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _client = null;
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
        _client?.Close();
        try
        {
            Task.WaitAll(new[] { _receiveLoop, _sweepLoop }.Where(t => t is not null).ToArray()!, TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Expected on cancellation.
        }

        _client?.Dispose();
    }
}
