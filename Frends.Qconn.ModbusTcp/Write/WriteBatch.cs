using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Write.Definitions;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Write;

/// <summary>Frends Task for multiple writes over a single TCP connection to one device.
/// Item-level Modbus exceptions are recorded but do not abort the batch.
/// A socket-level failure aborts the remaining items; retry (when configured) retries the whole batch.</summary>
public static class WriteBatch
{
    public static async Task<WriteBatchResult> WriteBatchData(
        [PropertyTab] WriteBatchInput input,
        [PropertyTab] WriteOptions options,
        CancellationToken cancellationToken)
    {
        WriteGuard.EnsureAllowed(options.AllowWrites);

        var totalSw = Stopwatch.StartNew();
        var key = new ConnectionKey(input.Host, input.Port, input.UnitId,
            options.TransportMode, TransportSecurity.None, null, null);
        var breaker = BreakerRegistry.Get(key, options.CircuitBreaker);

        var attempts = new List<AttemptRecord>();
        int maxAttempts = Math.Max(1, options.Retry.MaxAttempts);
        WriteBatchResult? attemptResult = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.CircuitBreaker.Enabled && !breaker.CanPass(BreakerRegistry.Clock))
            {
                var diag = new Diagnostics(0, 0, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, 0, 0);
                attemptResult = new WriteBatchResult(new Dictionary<string, WriteOutcome>(), diag,
                    new ErrorDetail(ErrorCategory.CircuitOpen, true,
                        $"Circuit open for {input.Host}:{input.Port}/UnitId={input.UnitId}."));
                attempts.Add(new AttemptRecord(attempt, 0, ErrorCategory.CircuitOpen, attemptResult.Error!.Message));
                break;
            }

            var attemptSw = Stopwatch.StartNew();
            attemptResult = await DoOneWriteBatchAsync(input, options, key, totalSw, breaker, cancellationToken)
                .ConfigureAwait(false);

            var category = attemptResult.Success ? ErrorCategory.None : attemptResult.Error!.Category;
            var modbusCode = attemptResult.Error?.ModbusExceptionCode;
            attempts.Add(new AttemptRecord(attempt, attemptSw.ElapsedMilliseconds, category,
                attemptResult.Error?.Message, modbusCode));

            if (attemptResult.Success)
            {
                breaker.RecordSuccess();
                break;
            }

            if (CircuitBreaker.CountsAsFailure(category, modbusCode))
                breaker.RecordFailure(BreakerRegistry.Clock);

            if (attempt >= maxAttempts || !RetryExecutor.ShouldRetry(category, modbusCode, options.Retry))
                break;

            await RetryExecutor.DelayAsync(
                RetryExecutor.ComputeBackoff(attempt, options.Retry), cancellationToken)
                .ConfigureAwait(false);
        }

        var finalDiag = RetryExecutor.AttachHistory(attemptResult!.Diagnostics, attempts, totalSw.ElapsedMilliseconds);
        var final = attemptResult.Success
            ? new WriteBatchResult(attemptResult.Items, finalDiag)
            : new WriteBatchResult(attemptResult.Items, finalDiag, attemptResult.Error!);

        EmitAudit(input, final, attempts.Count);

        if (!final.Success && options.ThrowOnFailure)
            throw new Exception(final.Error!.Message);
        return final;
    }

    private static async Task<WriteBatchResult> DoOneWriteBatchAsync(
        WriteBatchInput input, WriteOptions options, ConnectionKey key,
        Stopwatch totalSw, CircuitBreaker breaker,
        CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, WriteOutcome>();
        long connectTimeMs = 0;

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
        catch (Exception ex) when (ex is TimeoutException or SocketException)
        {
            var category = ex switch
            {
                TimeoutException te when te.Message.StartsWith("Acquire timed out", StringComparison.Ordinal) => ErrorCategory.Backpressure,
                TimeoutException => ErrorCategory.Timeout,
                SocketException se => MapSocketCategory(se),
                _ => ErrorCategory.Unexpected,
            };
            var diag = new Diagnostics(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, 0, 0);
            return new WriteBatchResult(items, diag,
                new ErrorDetail(category, true, ex.Message,
                    socketErrorCode: (ex as SocketException)?.SocketErrorCode.ToString()));
        }

        await using (lease)
        {
            using var cancelReg = cancellationToken.Register(() => lease.DisposeUnderlyingSocketForCancellation());

            foreach (var item in input.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort wireAddr = ModbusDecoder.TranslateAddress(item.StartAddress, options.AddressingMode);

                try
                {
                    if (item.DataType == ModbusDataType.Coils)
                    {
                        var bools = ModbusEncoder.EncodeBools(item.Values ?? throw new ArgumentException($"Item '{item.Name}': Values is null."));
                        await lease.Master.WriteMultipleCoilsAsync(input.UnitId, wireAddr, bools).ConfigureAwait(false);
                        items[item.Name] = new WriteOutcome((ushort)bools.Length);
                    }
                    else if (item.DataType == ModbusDataType.HoldingRegisters)
                    {
                        ushort[] regs;
                        if (item.ValueType == ModbusValueType.AsciiString)
                            regs = ModbusEncoder.EncodeAsciiString(
                                item.Values as string ?? throw new ArgumentException($"Item '{item.Name}': AsciiString requires string Values."),
                                item.NumberOfValues);
                        else
                            regs = ModbusEncoder.Encode(item.Values ?? throw new ArgumentException($"Item '{item.Name}': Values is null."),
                                item.ValueType, options.ByteOrder, item.Scale, item.Offset);
                        await lease.Master.WriteMultipleRegistersAsync(input.UnitId, wireAddr, regs).ConfigureAwait(false);
                        items[item.Name] = new WriteOutcome((ushort)regs.Length);
                    }
                    else
                    {
                        items[item.Name] = new WriteOutcome(new ErrorDetail(ErrorCategory.InvalidInput, false,
                            $"Item '{item.Name}': WriteBatch supports DataType Coils or HoldingRegisters only."));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    lease.Poison();
                    throw;
                }
                catch (SlaveException ex)
                {
                    if (CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, ex.SlaveExceptionCode))
                        breaker.RecordFailure(BreakerRegistry.Clock);
                    items[item.Name] = new WriteOutcome(new ErrorDetail(ErrorCategory.ModbusException,
                        ex.SlaveExceptionCode is 5 or 6 or 10 or 11, ex.Message,
                        modbusExceptionCode: ex.SlaveExceptionCode));
                }
                catch (ArgumentException ex)
                {
                    items[item.Name] = new WriteOutcome(new ErrorDetail(ErrorCategory.InvalidInput, false, ex.Message));
                }
                catch (Exception ex) when (ex is TimeoutException or SocketException)
                {
                    lease.Poison();
                    var category = ex switch
                    {
                        TimeoutException => ErrorCategory.Timeout,
                        SocketException se => MapSocketCategory(se),
                        _ => ErrorCategory.Unexpected,
                    };
                    var diag = new Diagnostics(connectTimeMs, 0, totalSw.ElapsedMilliseconds,
                        input.Host, input.Port, input.UnitId, wireAddr, 0);
                    return new WriteBatchResult(items, diag,
                        new ErrorDetail(category, true, ex.Message,
                            socketErrorCode: (ex as SocketException)?.SocketErrorCode.ToString()));
                }
                catch (IOException ex) when (ex.InnerException is SocketException inner)
                {
                    lease.Poison();
                    var diag = new Diagnostics(connectTimeMs, 0, totalSw.ElapsedMilliseconds,
                        input.Host, input.Port, input.UnitId, wireAddr, 0);
                    return new WriteBatchResult(items, diag,
                        new ErrorDetail(MapSocketCategory(inner), true, ex.Message,
                            socketErrorCode: inner.SocketErrorCode.ToString()));
                }
                catch (Exception ex)
                {
                    lease.Poison();
                    var diag = new Diagnostics(connectTimeMs, 0, totalSw.ElapsedMilliseconds,
                        input.Host, input.Port, input.UnitId, wireAddr, 0);
                    return new WriteBatchResult(items, diag,
                        new ErrorDetail(ErrorCategory.Unexpected, false, ex.Message));
                }
            }

            var finalDiag = new Diagnostics(connectTimeMs, 0, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, 0, 0);
            return new WriteBatchResult(items, finalDiag);
        }
    }

    private static void EmitAudit(WriteBatchInput input, WriteBatchResult result, int attemptCount)
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
            Operation = "WriteBatch",
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
            AttemptCount = Math.Max(1, attemptCount),
            TotalTimeMs = result.Diagnostics.TotalTimeMs,
            ValuesWritten = input.Items,
        });
    }

    private static ErrorCategory MapSocketCategory(SocketException ex) =>
        ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => ErrorCategory.ConnectionRefused,
            SocketError.HostUnreachable => ErrorCategory.HostUnreachable,
            SocketError.NetworkUnreachable => ErrorCategory.HostUnreachable,
            SocketError.TimedOut => ErrorCategory.Timeout,
            _ => ErrorCategory.SocketError,
        };
}
