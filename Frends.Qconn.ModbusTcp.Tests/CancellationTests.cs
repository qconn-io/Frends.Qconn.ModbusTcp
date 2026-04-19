using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using ReadTask = Frends.Qconn.ModbusTcp.Read.Read;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Test 9: CancellationToken propagation — OperationCanceledException must not be swallowed.</summary>
public class CancellationTests
{
    [Fact]
    public async Task Cancellation_Before_Connect_Propagates_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var input = new Input
        {
            Host = "10.255.255.1",
            Port = 502,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 0,
            NumberOfValues = 1,
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ReadTask.ReadData(input, new Options { ConnectTimeoutMs = 30000 }, cts.Token));
    }

    [Fact]
    public async Task Cancellation_After_Connect_Propagates_OperationCanceledException()
    {
        // TCP listener that accepts but never responds — Modbus read will block
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync();
            await Task.Delay(30000); // hold open indefinitely
        });

        using var cts = new CancellationTokenSource(200);

        var input = new Input
        {
            Host = "127.0.0.1",
            Port = port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 0,
            NumberOfValues = 1,
        };

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => ReadTask.ReadData(input, new Options { ConnectTimeoutMs = 5000, ReadTimeoutMs = 30000 }, cts.Token));
        }
        finally
        {
            listener.Stop();
        }
    }
}
