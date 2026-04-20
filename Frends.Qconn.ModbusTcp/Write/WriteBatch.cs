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
using Frends.Qconn.ModbusTcp.Write.Definitions;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Write;

/// <summary>Frends Task for multiple writes over a single TCP connection to one device.
/// Item-level Modbus exceptions are recorded but do not abort the batch.
/// A socket-level failure aborts the remaining items and sets Success=false.</summary>
public static class WriteBatch
{
    public static async Task<WriteBatchResult> WriteBatchData(
        [PropertyTab] WriteBatchInput input,
        [PropertyTab] WriteOptions options,
        CancellationToken cancellationToken)
    {
        WriteGuard.EnsureAllowed(options.AllowWrites);

        var totalSw = Stopwatch.StartNew();
        var items = new Dictionary<string, WriteOutcome>();
        long connectTimeMs = 0;

        var key = new ConnectionKey(input.Host, input.Port, input.UnitId,
            options.TransportMode, TransportSecurity.None, null, null);
        var breaker = BreakerRegistry.Get(key, options.CircuitBreaker);

        if (options.CircuitBreaker.Enabled && !breaker.CanPass(BreakerRegistry.Clock))
        {
            var diag = new Diagnostics(0, 0, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, 0, 0);
            var result = new WriteBatchResult(items, diag,
                new ErrorDetail(ErrorCategory.CircuitOpen, true,
                    $"Circuit open for {input.Host}:{input.Port}/UnitId={input.UnitId}."));
            EmitAudit(input, result);
            if (options.ThrowOnFailure) throw new Exception(result.Error!.Message);
            return result;
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
        catch (Exception ex) when (ex is TimeoutException or SocketException)
        {
            breaker.RecordFailure(BreakerRegistry.Clock);
            var category = ex switch
            {
                TimeoutException te when te.Message.StartsWith("Acquire timed out", StringComparison.Ordinal) => ErrorCategory.Backpressure,
                TimeoutException => ErrorCategory.Timeout,
                SocketException se => MapSocketCategory(se),
                _ => ErrorCategory.Unexpected,
            };
            var diag = new Diagnostics(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, 0, 0);
            var result = new WriteBatchResult(items, diag,
                new ErrorDetail(category, true, ex.Message,
                    socketErrorCode: (ex as SocketException)?.SocketErrorCode.ToString()));
            EmitAudit(input, result);
            if (options.ThrowOnFailure) throw new Exception(result.Error!.Message);
            return result;
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
                    breaker.RecordFailure(BreakerRegistry.Clock);
                    var category = ex switch
                    {
                        TimeoutException => ErrorCategory.Timeout,
                        SocketException se => MapSocketCategory(se),
                        _ => ErrorCategory.Unexpected,
                    };
                    var diag = new Diagnostics(connectTimeMs, 0, totalSw.ElapsedMilliseconds,
                        input.Host, input.Port, input.UnitId, wireAddr, 0);
                    var result = new WriteBatchResult(items, diag,
                        new ErrorDetail(category, true, ex.Message,
                            socketErrorCode: (ex as SocketException)?.SocketErrorCode.ToString()));
                    EmitAudit(input, result);
                    if (options.ThrowOnFailure) throw new Exception(result.Error!.Message);
                    return result;
                }
                catch (Exception ex)
                {
                    lease.Poison();
                    var diag = new Diagnostics(connectTimeMs, 0, totalSw.ElapsedMilliseconds,
                        input.Host, input.Port, input.UnitId, wireAddr, 0);
                    var result = new WriteBatchResult(items, diag,
                        new ErrorDetail(ErrorCategory.Unexpected, false, ex.Message));
                    EmitAudit(input, result);
                    if (options.ThrowOnFailure) throw;
                    return result;
                }
            }

            breaker.RecordSuccess();
            var finalDiag = new Diagnostics(connectTimeMs, 0, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, 0, 0);
            var ok = new WriteBatchResult(items, finalDiag);
            EmitAudit(input, ok);
            return ok;
        }
    }

    private static void EmitAudit(WriteBatchInput input, WriteBatchResult result)
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
            AttemptCount = 1,
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
