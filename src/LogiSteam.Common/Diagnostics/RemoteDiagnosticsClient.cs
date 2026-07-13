using System.Net.Sockets;
using System.Text;

namespace LogiSteam.Common.Diagnostics;

/// <summary>
/// Client-side counterpart to <see cref="RemoteDiagnosticsServer"/> -- speaks the same trivial
/// protocol as `tools/query-diagnostics.ps1` (connect, read until the other side closes, that's
/// the whole response), so GameSide's Diagnostics window can fetch a Wheel PC's live snapshot
/// in-app instead of requiring a PowerShell script.
/// </summary>
public static class RemoteDiagnosticsClient
{
    public static async Task<string> FetchAsync(string ipAddress, int port, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        var connectTask = client.ConnectAsync(ipAddress, port, ct).AsTask();
        var timeoutTask = Task.Delay(timeout ?? TimeSpan.FromSeconds(5), ct);
        if (await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
        {
            throw new TimeoutException($"Timed out connecting to {ipAddress}:{port}.");
        }

        await connectTask.ConfigureAwait(false); // observe/rethrow any connect exception

        using var stream = client.GetStream();
        using var memory = new MemoryStream();
        var buffer = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            memory.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }
}
