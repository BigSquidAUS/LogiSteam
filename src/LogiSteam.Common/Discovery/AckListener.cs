using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LogiSteam.Common.Discovery;

/// <summary>
/// Runs on the Wheel PC. Listens for <see cref="AckMessage"/>s from Game PCs, so the Wheel PC can
/// show whether anyone currently sees its beacon -- not just that it's broadcasting. Mirrors
/// <see cref="BeaconListener"/>'s structure.
/// </summary>
public sealed class AckListener : IDisposable
{
    private readonly ConcurrentDictionary<IPAddress, DiscoveredGamePc> _seen = new();
    private CancellationTokenSource _cts = new();
    private UdpClient? _client;
    private Task? _receiveLoop;
    private Task? _sweepLoop;

    public event Action<DiscoveredGamePc>? GamePcSeenOrUpdated;
    public event Action<IPAddress>? GamePcLost;

    public IReadOnlyCollection<DiscoveredGamePc> CurrentlyVisible => _seen.Values.ToArray();

    public void Start()
    {
        if (_client is not null)
        {
            return;
        }

        _client = new UdpClient(AddressFamily.InterNetwork);
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, BeaconProtocol.AckPort));

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
                if (!AckMessage.TryParse(json, out var message) || message is null)
                {
                    continue;
                }

                var discovered = new DiscoveredGamePc(result.RemoteEndPoint.Address, message, DateTime.UtcNow);
                _seen[discovered.Address] = discovered;
                GamePcSeenOrUpdated?.Invoke(discovered);
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

            var cutoff = DateTime.UtcNow - BeaconProtocol.AckStaleAfter;
            foreach (var kvp in _seen)
            {
                if (kvp.Value.LastSeenUtc < cutoff && _seen.TryRemove(kvp.Key, out _))
                {
                    GamePcLost?.Invoke(kvp.Key);
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
