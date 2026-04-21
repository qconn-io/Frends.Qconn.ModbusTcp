using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Frends.Qconn.ModbusTcp.Tests.Helpers;

/// <summary>Raw Modbus TCP server that injects failures for the first <c>failCount</c> requests/connections,
/// then returns a valid minimal success response. Two failure modes:
/// <list type="bullet">
/// <item><see cref="FaultMode.SlaveBusy"/> — returns Modbus exception code 6 (SlaveBusy) per request; connection stays alive.</item>
/// <item><see cref="FaultMode.Disconnect"/> — closes the TCP connection with RST per connection; use for batch tasks where SlaveBusy is item-level and does not abort the batch.</item>
/// </list>
/// </summary>
internal sealed class FaultInjectionServer : IDisposable
{
    internal enum FaultMode { SlaveBusy, Disconnect }

    private readonly TcpListener _listener;
    private readonly FaultMode _mode;
    private int _remaining;
    private bool _disposed;

    public int Port { get; }

    public FaultInjectionServer(int failCount, FaultMode mode = FaultMode.SlaveBusy)
    {
        _mode = mode;
        _remaining = failCount;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_disposed)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                _ = HandleClientAsync(client);
            }
            catch
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            // Disconnect mode: close the connection with RST before exchanging any data.
            // This causes a SocketException on the client side, aborting the batch/task and triggering retry.
            if (_mode == FaultMode.Disconnect && Interlocked.Decrement(ref _remaining) >= 0)
            {
                client.Client.LingerState = new System.Net.Sockets.LingerOption(true, 0);
                return;
            }

            var stream = client.GetStream();
            var header = new byte[6];

            while (true)
            {
                // Read 6-byte MBAP header.
                if (!await ReadExactAsync(stream, header, 6).ConfigureAwait(false))
                    return;

                ushort tid = (ushort)((header[0] << 8) | header[1]);
                ushort pduLen = (ushort)((header[4] << 8) | header[5]);
                if (pduLen < 2)
                    return;

                var pdu = new byte[pduLen];
                if (!await ReadExactAsync(stream, pdu, pduLen).ConfigureAwait(false))
                    return;

                byte unitId = pdu[0];
                byte fc = pdu[1];
                byte[] response;

                // SlaveBusy mode: return exception code 6 per request until counter is exhausted.
                if (_mode == FaultMode.SlaveBusy && Interlocked.Decrement(ref _remaining) >= 0)
                {
                    response = new byte[] { header[0], header[1], 0, 0, 0, 3, unitId, (byte)(fc | 0x80), 6 };
                }
                else
                {
                    response = BuildSuccessResponse(tid, unitId, fc, pdu);
                }

                await stream.WriteAsync(response).ConfigureAwait(false);
            }
        }
    }

    private static byte[] BuildSuccessResponse(ushort tid, byte unitId, byte fc, byte[] pdu)
    {
        byte thi = (byte)(tid >> 8);
        byte tlo = (byte)(tid & 0xFF);

        switch (fc)
        {
            case 1: // Read Coils
            case 2: // Read Discrete Inputs
            {
                // Quantity is at pdu[4..5] (pdu[2..3] is starting address).
                ushort count = (pdu.Length >= 6) ? (ushort)((pdu[4] << 8) | pdu[5]) : (ushort)1;
                int byteCount = (count + 7) / 8;
                var body = new byte[3 + byteCount];
                body[0] = unitId; body[1] = fc; body[2] = (byte)byteCount;
                int len = body.Length;
                return BuildMbap(thi, tlo, len, body);
            }
            case 3: // Read Holding Registers
            case 4: // Read Input Registers
            {
                // Quantity is at pdu[4..5] (pdu[2..3] is starting address).
                ushort count = (pdu.Length >= 6) ? (ushort)((pdu[4] << 8) | pdu[5]) : (ushort)1;
                var body = new byte[3 + count * 2];
                body[0] = unitId; body[1] = fc; body[2] = (byte)(count * 2);
                return BuildMbap(thi, tlo, body.Length, body);
            }
            case 5: // Write Single Coil — echo request
            case 6: // Write Single Register — echo request
            {
                var body = new byte[] { unitId, fc, pdu[2], pdu[3], pdu[4], pdu[5] };
                return BuildMbap(thi, tlo, 6, body);
            }
            case 15: // Write Multiple Coils
            case 16: // Write Multiple Registers
            {
                var body = new byte[] { unitId, fc, pdu[2], pdu[3], pdu[4], pdu[5] };
                return BuildMbap(thi, tlo, 6, body);
            }
            case 23: // Read/Write Multiple Registers
            {
                // Read quantity is at pdu[4..5] (pdu[2..3] is read starting address).
                ushort readCount = (pdu.Length >= 6) ? (ushort)((pdu[4] << 8) | pdu[5]) : (ushort)1;
                var body = new byte[3 + readCount * 2];
                body[0] = unitId; body[1] = fc; body[2] = (byte)(readCount * 2);
                return BuildMbap(thi, tlo, body.Length, body);
            }
            default:
            {
                // Generic echo of first 6 bytes.
                var body = new byte[] { unitId, fc, 0, 0, 0, 0 };
                return BuildMbap(thi, tlo, 6, body);
            }
        }
    }

    private static byte[] BuildMbap(byte thi, byte tlo, int pduLen, byte[] pdu)
    {
        var frame = new byte[6 + pduLen];
        frame[0] = thi; frame[1] = tlo;
        frame[2] = 0; frame[3] = 0;
        frame[4] = (byte)(pduLen >> 8); frame[5] = (byte)(pduLen & 0xFF);
        Buffer.BlockCopy(pdu, 0, frame, 6, pduLen);
        return frame;
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buf, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buf.AsMemory(read, count - read)).ConfigureAwait(false);
            if (n == 0) return false;
            read += n;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener.Stop();
    }
}
