using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LogiSteam.Common.Diagnostics;

/// <summary>
/// A minimal TCP endpoint that responds to any incoming connection with a JSON snapshot of the
/// app's current live state, built on demand by <paramref name="buildSnapshotJson"/>. Exists so a
/// remote troubleshooter can query real-time state directly over the LAN instead of relying on
/// the user relaying copy-pasted output or running one-off diagnostic scripts.
///
/// Deliberately plain sockets, not <see cref="System.Net.HttpListener"/>: binding HttpListener to
/// a non-loopback prefix requires either running elevated or a one-time `netsh http add urlacl`
/// reservation, whereas a normal TcpListener on a port above 1024 needs neither. Read-only by
/// design -- the response is the entire interaction, there is no way to send commands back that
/// change any state, which keeps the exposure surface of an unauthenticated LAN endpoint minimal.
/// </summary>
public sealed class RemoteDiagnosticsServer(int port, Func<string> buildSnapshotJson) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    private Task? _loop;

    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _loop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var json = buildSnapshotJson();
                var bytes = Encoding.UTF8.GetBytes(json);
                await client.GetStream().WriteAsync(bytes, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // Best-effort; the querying side just gets a truncated/empty response and can retry.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Expected on cancellation.
        }

        _cts.Dispose();
    }
}
