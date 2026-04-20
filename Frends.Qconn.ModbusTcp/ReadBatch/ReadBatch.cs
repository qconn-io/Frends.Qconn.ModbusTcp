using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.ReadBatch.Definitions;
using NModbus;

namespace Frends.Qconn.ModbusTcp.ReadBatch;

/// <summary>Frends Task for reading multiple register blocks from a Modbus TCP slave over a single TCP connection.</summary>
public static class ReadBatch
{
    /// <summary>
    /// Opens one TCP connection (or reuses a pooled one) and executes all reads in Input.Items sequentially.
    /// A Modbus exception on one item fails that item but does not abort the batch.
    /// A socket-level failure aborts the entire batch and sets BatchResult.Success = false.
    /// </summary>
    /// <param name="input">Connection info and list of register reads to perform.</param>
    /// <param name="options">Timeout, byte order, pool, and error-handling options (shared across all items).</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>BatchResult with per-item outcomes and batch-level diagnostics.</returns>
    public static async Task<BatchResult> ReadBatchData(
        [PropertyTab] BatchInput input,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();
        var items = new Dictionary<string, ReadOutcome>();
        long connectTimeMs = 0;

        var key = new ConnectionKey(input.Host, input.Port, input.UnitId,
            options.TransportMode, TransportSecurity.None, null, null);
        var breaker = BreakerRegistry.Get(key, options.CircuitBreaker);

        // Breaker short-circuit before opening a socket.
        if (options.CircuitBreaker.Enabled && !breaker.CanPass(BreakerRegistry.Clock))
        {
            var diag = MakeDiag(0, 0, totalSw.ElapsedMilliseconds, input, 0, 0);
            return Audit(input, new BatchResult(items, diag,
                new ErrorDetail(ErrorCategory.CircuitOpen, true,
                    $"Circuit open for {input.Host}:{input.Port}/UnitId={input.UnitId}.")));
        }

        ModbusLease? lease;
        var acquireSw = Stopwatch.StartNew();
        try
        {
            lease = await ModbusSession.AcquireAsync(key, options, cancellationToken).ConfigureAwait(false);
            connectTimeMs = lease.ConnectTimeMs;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            var category = ex.Message.StartsWith("Acquire timed out", StringComparison.Ordinal)
                ? ErrorCategory.Backpressure : ErrorCategory.Timeout;
            breaker.RecordFailure(BreakerRegistry.Clock);
            var diag = MakeDiag(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds, input, 0, 0);
            return Audit(input, new BatchResult(items, diag,
                new ErrorDetail(category, true, ex.Message)));
        }
        catch (SocketException ex)
        {
            breaker.RecordFailure(BreakerRegistry.Clock);
            var diag = MakeDiag(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds, input, 0, 0);
            return Audit(input, new BatchResult(items, diag,
                new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                    socketErrorCode: ex.SocketErrorCode.ToString())));
        }

        await using (lease)
        {
            using var cancelReg = cancellationToken.Register(() => lease.DisposeUnderlyingSocketForCancellation());

            foreach (var item in input.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readSw = Stopwatch.StartNew();

                bool isCoilType = item.DataType is ModbusDataType.Coils or ModbusDataType.DiscreteInputs;
                ushort wireCount = isCoilType
                    ? item.NumberOfValues
                    : ModbusDecoder.ComputeRegisterCount(item.ValueType, item.NumberOfValues);
                ushort wireAddr = ModbusDecoder.TranslateAddress(item.StartAddress, options.AddressingMode);

                int maxCount = isCoilType ? 2000 : 125;
                if (item.NumberOfValues == 0 || wireCount > maxCount)
                {
                    items[item.Name] = new ReadOutcome(
                        new ErrorDetail(ErrorCategory.InvalidInput, false,
                            $"Item '{item.Name}': invalid NumberOfValues or count exceeds maximum {maxCount}."));
                    continue;
                }

                try
                {
                    object decoded;
                    ushort[]? rawRegisters = null;

                    if (item.DataType == ModbusDataType.Coils)
                    {
                        bool[] coils = await lease.Master.ReadCoilsAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                        decoded = coils;
                    }
                    else if (item.DataType == ModbusDataType.DiscreteInputs)
                    {
                        bool[] disc = await lease.Master.ReadInputsAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                        decoded = disc;
                    }
                    else if (item.DataType == ModbusDataType.HoldingRegisters)
                    {
                        ushort[] regs = await lease.Master.ReadHoldingRegistersAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                        rawRegisters = regs;
                        decoded = ModbusDecoder.Decode(regs, item.ValueType, options.ByteOrder,
                            item.NumberOfValues, item.Scale, item.Offset);
                    }
                    else // InputRegisters
                    {
                        ushort[] regs = await lease.Master.ReadInputRegistersAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                        rawRegisters = regs;
                        decoded = ModbusDecoder.Decode(regs, item.ValueType, options.ByteOrder,
                            item.NumberOfValues, item.Scale, item.Offset);
                    }

                    items[item.Name] = new ReadOutcome(decoded, rawRegisters);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    lease.Poison();
                    throw;
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    lease.Poison();
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (SlaveException ex)
                {
                    items[item.Name] = new ReadOutcome(
                        new ErrorDetail(ErrorCategory.ModbusException, IsTransientModbus(ex.SlaveExceptionCode),
                            ex.Message, modbusExceptionCode: ex.SlaveExceptionCode));
                }
                catch (FormatException ex)
                {
                    items[item.Name] = new ReadOutcome(new ErrorDetail(ErrorCategory.DecodingError, false, ex.Message));
                }
                catch (TimeoutException ex)
                {
                    lease.Poison();
                    breaker.RecordFailure(BreakerRegistry.Clock);
                    var diag = MakeDiag(connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                    return Audit(input, new BatchResult(items, diag,
                        new ErrorDetail(ErrorCategory.Timeout, true, ex.Message)));
                }
                catch (SocketException ex)
                {
                    lease.Poison();
                    breaker.RecordFailure(BreakerRegistry.Clock);
                    var diag = MakeDiag(connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                    return Audit(input, new BatchResult(items, diag,
                        new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                            socketErrorCode: ex.SocketErrorCode.ToString())));
                }
                catch (Exception ex)
                {
                    lease.Poison();
                    var diag = MakeDiag(connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                    return Audit(input, new BatchResult(items, diag,
                        new ErrorDetail(ErrorCategory.Unexpected, false, ex.Message)));
                }
            }

            breaker.RecordSuccess();
            var finalDiag = MakeDiag(connectTimeMs, 0, totalSw.ElapsedMilliseconds, input, 0, 0);
            return Audit(input, new BatchResult(items, finalDiag));
        }
    }

    private static Diagnostics MakeDiag(long connectMs, long readMs, long totalMs,
        BatchInput input, ushort wireAddr, ushort wireCount) =>
        new(connectMs, readMs, totalMs, input.Host, input.Port, input.UnitId, wireAddr, wireCount);

    private static BatchResult Audit(BatchInput input, BatchResult result)
    {
        var ctx = AuditRouter.AgentContext;
        AuditRouter.Emit(new ModbusAuditEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            AgentName = ctx.AgentName,
            EnvironmentName = ctx.EnvironmentName,
            ProcessName = ctx.ProcessName,
            ProcessInstanceId = ctx.ProcessInstanceId,
            InitiatedBy = ctx.InitiatedBy,
            Operation = "ReadBatch",
            Host = input.Host,
            Port = input.Port,
            UnitId = input.UnitId,
            FunctionCode = 0,
            StartAddress = 0,
            Count = (ushort)(input.Items?.Count ?? 0),
            TransportSecurity = TransportSecurity.None,
            Success = result.Success,
            ErrorCategory = result.Error?.Category.ToString(),
            ModbusExceptionCode = result.Error?.ModbusExceptionCode,
            AttemptCount = 1,
            TotalTimeMs = result.Diagnostics.TotalTimeMs,
            ValuesWritten = null,
        });
        return result;
    }

    private static ErrorCategory MapSocketCategory(SocketException ex) =>
        ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused  => ErrorCategory.ConnectionRefused,
            SocketError.HostUnreachable    => ErrorCategory.HostUnreachable,
            SocketError.NetworkUnreachable => ErrorCategory.HostUnreachable,
            SocketError.TimedOut           => ErrorCategory.Timeout,
            _                              => ErrorCategory.SocketError,
        };

    private static bool IsTransientSocket(SocketException ex) =>
        ex.SocketErrorCode is
            SocketError.ConnectionRefused or
            SocketError.HostUnreachable or
            SocketError.NetworkUnreachable or
            SocketError.TimedOut;

    private static bool IsTransientModbus(int code) => code is 5 or 6 or 10 or 11;
}
