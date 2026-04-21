using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.ReadBatch.Definitions;
using Frends.Qconn.ModbusTcp.Tests.Helpers;
using Frends.Qconn.ModbusTcp.Write;
using Frends.Qconn.ModbusTcp.Write.Definitions;
using Xunit;
using ReadTask = Frends.Qconn.ModbusTcp.Read.Read;
using ReadBatchTask = Frends.Qconn.ModbusTcp.ReadBatch.ReadBatch;
using static Frends.Qconn.ModbusTcp.Tests.Helpers.FaultInjectionServer;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Tests that retry is correctly wired into all tasks (WP2).
/// All tests use Disconnect (RST) fault injection because NModbus handles SlaveBusy (code 6)
/// internally at the transport layer before our task code ever sees it.</summary>
public class RetryIntegrationTests : IDisposable
{
    public RetryIntegrationTests()
    {
        // Make retries instant so tests don't sleep.
        RetryExecutor.Delay = (_, _) => Task.CompletedTask;
        ConnectionPool.ResetForTests();
    }

    public void Dispose()
    {
        RetryExecutor.Delay = (ts, ct) => Task.Delay(ts, ct);
        ConnectionPool.ResetForTests();
    }

    private static RetryOptions SocketRetryOpts(int maxAttempts = 3) =>
        new() { MaxAttempts = maxAttempts, RetryOn = RetryableCategories.SocketError, InitialBackoffMs = 1 };

    // ─── Read.ReadData ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadData_Retries_On_Connection_Error_And_Succeeds()
    {
        using var server = new FaultInjectionServer(failCount: 2, FaultMode.Disconnect);

        var result = await ReadTask.ReadData(
            new Input { Host = "127.0.0.1", Port = server.Port, UnitId = 1, StartAddress = 0, NumberOfValues = 1 },
            new Options { Retry = SocketRetryOpts() }, CancellationToken.None);

        Assert.True(result.Success, $"Error: {result.Error?.Message}");
        Assert.NotNull(result.Diagnostics.AttemptHistory);
        Assert.Equal(3, result.Diagnostics.AttemptHistory!.Count);
        Assert.Equal(3, result.Diagnostics.AttemptCount);
    }

    // ─── ReadBatch.ReadBatchData ─────────────────────────────────────────────────

    [Fact]
    public async Task ReadBatch_Retries_On_Connection_Error_And_Succeeds()
    {
        using var server = new FaultInjectionServer(failCount: 2, FaultMode.Disconnect);

        var result = await ReadBatchTask.ReadBatchData(
            new BatchInput
            {
                Host = "127.0.0.1", Port = server.Port, UnitId = 1,
                Items = new List<BatchReadItem>
                {
                    new BatchReadItem { Name = "r1", DataType = ModbusDataType.HoldingRegisters, StartAddress = 0, NumberOfValues = 1 },
                },
            },
            new Options { Retry = SocketRetryOpts() }, CancellationToken.None);

        Assert.True(result.Success, $"Error: {result.Error?.Message}");
        Assert.NotNull(result.Diagnostics.AttemptHistory);
        Assert.Equal(3, result.Diagnostics.AttemptHistory!.Count);
    }

    // ─── WriteSingleRegister ─────────────────────────────────────────────────────

    [Fact]
    public async Task WriteSingleRegister_Retries_On_Connection_Error_And_Succeeds()
    {
        using var server = new FaultInjectionServer(failCount: 2, FaultMode.Disconnect);

        var result = await WriteSingleRegister.WriteData(
            new WriteSingleRegisterInput { Host = "127.0.0.1", Port = server.Port, UnitId = 1, StartAddress = 5, Value = 99 },
            new WriteOptions { ThrowOnFailure = false, Retry = SocketRetryOpts() }, CancellationToken.None);

        Assert.True(result.Success, $"Error: {result.Error?.Message}");
        Assert.NotNull(result.Diagnostics.AttemptHistory);
        Assert.Equal(3, result.Diagnostics.AttemptHistory!.Count);
    }

    // ─── WriteSingleCoil ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteSingleCoil_Retries_On_Connection_Error_And_Succeeds()
    {
        using var server = new FaultInjectionServer(failCount: 2, FaultMode.Disconnect);

        var result = await WriteSingleCoil.WriteData(
            new WriteSingleCoilInput { Host = "127.0.0.1", Port = server.Port, UnitId = 1, StartAddress = 0, Value = true },
            new WriteOptions { ThrowOnFailure = false, Retry = SocketRetryOpts() }, CancellationToken.None);

        Assert.True(result.Success, $"Error: {result.Error?.Message}");
        Assert.NotNull(result.Diagnostics.AttemptHistory);
        Assert.Equal(3, result.Diagnostics.AttemptHistory!.Count);
    }

    // ─── WriteMultiple ───────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteMultiple_Retries_On_Connection_Error_And_Succeeds()
    {
        using var server = new FaultInjectionServer(failCount: 2, FaultMode.Disconnect);

        var result = await WriteMultiple.WriteData(
            new WriteInput
            {
                Host = "127.0.0.1", Port = server.Port, UnitId = 1,
                DataType = ModbusDataType.HoldingRegisters, StartAddress = 10,
                NumberOfValues = 2, ValueType = ModbusValueType.UInt16, Values = new[] { 1, 2 },
            },
            new WriteOptions { ThrowOnFailure = false, Retry = SocketRetryOpts() }, CancellationToken.None);

        Assert.True(result.Success, $"Error: {result.Error?.Message}");
        Assert.NotNull(result.Diagnostics.AttemptHistory);
        Assert.Equal(3, result.Diagnostics.AttemptHistory!.Count);
    }

    // ─── ReadWriteMultiple ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReadWriteMultiple_Retries_On_Connection_Error_And_Succeeds()
    {
        using var server = new FaultInjectionServer(failCount: 2, FaultMode.Disconnect);

        var result = await ReadWriteMultiple.ReadWriteData(
            new ReadWriteMultipleInput
            {
                Host = "127.0.0.1", Port = server.Port, UnitId = 1,
                ReadStartAddress = 0, ReadRegisterCount = 1,
                WriteStartAddress = 0, WriteRegisters = new ushort[] { 100 },
            },
            new WriteOptions { ThrowOnFailure = false, Retry = SocketRetryOpts() }, CancellationToken.None);

        Assert.True(result.Success, $"Error: {result.Error?.Message}");
        Assert.NotNull(result.Diagnostics.AttemptHistory);
        Assert.Equal(3, result.Diagnostics.AttemptHistory!.Count);
    }

    // ─── WriteBatch ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteBatch_Retries_On_Connection_Error_And_Succeeds()
    {
        using var server = new FaultInjectionServer(failCount: 2, FaultMode.Disconnect);

        var result = await WriteBatch.WriteBatchData(
            new WriteBatchInput
            {
                Host = "127.0.0.1", Port = server.Port, UnitId = 1,
                Items = new List<WriteBatchItem>
                {
                    new WriteBatchItem { Name = "w1", DataType = ModbusDataType.HoldingRegisters, StartAddress = 0, NumberOfValues = 1, ValueType = ModbusValueType.UInt16, Values = new[] { 7 } },
                },
            },
            new WriteOptions { ThrowOnFailure = false, Retry = SocketRetryOpts() }, CancellationToken.None);

        Assert.True(result.Success, $"Error: {result.Error?.Message}");
        Assert.NotNull(result.Diagnostics.AttemptHistory);
        Assert.Equal(3, result.Diagnostics.AttemptHistory!.Count);
    }

    // ─── MaxAttempts = 1 means no retry ─────────────────────────────────────────

    [Fact]
    public async Task WriteSingleRegister_No_Retry_When_MaxAttempts_Is_1()
    {
        using var server = new FaultInjectionServer(failCount: 1, FaultMode.Disconnect);

        var result = await WriteSingleRegister.WriteData(
            new WriteSingleRegisterInput { Host = "127.0.0.1", Port = server.Port, UnitId = 1, StartAddress = 5, Value = 99 },
            new WriteOptions { ThrowOnFailure = false, Retry = new RetryOptions { MaxAttempts = 1 } }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Diagnostics.AttemptHistory);
        Assert.Equal(1, result.Diagnostics.AttemptCount);
    }
}
