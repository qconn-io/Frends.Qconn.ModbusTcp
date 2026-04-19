using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>TCP connection helpers with timeout and cancellation support.</summary>
internal static class ModbusConnection
{
    /// <summary>Connects to a Modbus TCP slave with an independent connect timeout.
    /// Throws TimeoutException if the timeout expires before the connection is established.
    /// Throws SocketException if the connection is refused or the host is unreachable.
    /// </summary>
    internal static async Task<(TcpClient Client, long ConnectTimeMs)> ConnectAsync(
        string host, int port, int connectTimeoutMs, CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        var sw = Stopwatch.StartNew();

        // Race the connect against an independent timeout so ConnectTimeoutMs is not gated on the OS TCP timeout.
        var connectTask = tcpClient.ConnectAsync(host, port, cancellationToken).AsTask();
        var timeoutTask = Task.Delay(connectTimeoutMs, cancellationToken);

        var winner = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
        long elapsedMs = sw.ElapsedMilliseconds;

        if (winner == timeoutTask)
        {
            tcpClient.Dispose();
            throw new TimeoutException($"TCP connect to {host}:{port} timed out after {connectTimeoutMs} ms.");
        }

        // Propagate any SocketException or OperationCanceledException from the connect task.
        await connectTask.ConfigureAwait(false);
        return (tcpClient, elapsedMs);
    }
}
