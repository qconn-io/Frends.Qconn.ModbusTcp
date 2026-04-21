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

/// <summary>Frends Task for Modbus FC23 (Read/Write Multiple Registers): atomic write+read in one request.</summary>
public static class ReadWriteMultiple
{
    /// <summary>Executes a Modbus FC23 request. Writes are applied first, then the read block is returned.
    /// Retry (when Options.Retry.MaxAttempts > 1) retries the entire FC23 request as a unit.</summary>
    public static async Task<ReadWriteMultipleResult> ReadWriteData(
        [PropertyTab] ReadWriteMultipleInput input,
        [PropertyTab] WriteOptions options,
        CancellationToken cancellationToken)
    {
        WriteGuard.EnsureAllowed(options.AllowWrites);

        if (input.WriteRegisters is null || input.WriteRegisters.Length == 0)
            throw new ArgumentException("ReadWriteMultiple requires WriteRegisters to contain at least one value.", nameof(input));

        var totalSw = Stopwatch.StartNew();
        ushort readAddr = ModbusDecoder.TranslateAddress(input.ReadStartAddress, options.AddressingMode);
        ushort writeAddr = ModbusDecoder.TranslateAddress(input.WriteStartAddress, options.AddressingMode);
        ushort readCount = input.ReadRegisterCount;
        ushort writeCount = (ushort)input.WriteRegisters.Length;

        var key = new ConnectionKey(input.Host, input.Port, input.UnitId,
            options.TransportMode, TransportSecurity.None, null, null);
        var breaker = BreakerRegistry.Get(key, options.CircuitBreaker);

        var attempts = new List<AttemptRecord>();
        int maxAttempts = Math.Max(1, options.Retry.MaxAttempts);
        ReadWriteMultipleResult? attemptResult = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.CircuitBreaker.Enabled && !breaker.CanPass(BreakerRegistry.Clock))
            {
                var diag = new Diagnostics(0, 0, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, readAddr, readCount);
                attemptResult = new ReadWriteMultipleResult(
                    new ErrorDetail(ErrorCategory.CircuitOpen, true,
                    $"Circuit open for {input.Host}:{input.Port}/UnitId={input.UnitId}."), diag);
                attempts.Add(new AttemptRecord(attempt, 0, ErrorCategory.CircuitOpen, attemptResult.Error!.Message));
                break;
            }

            var attemptSw = Stopwatch.StartNew();
            attemptResult = await DoOneReadWriteMultipleAsync(
                input, options, key, totalSw, readAddr, writeAddr, readCount, writeCount, cancellationToken)
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
            ? new ReadWriteMultipleResult(attemptResult.ReadRegisters!, finalDiag)
            : new ReadWriteMultipleResult(attemptResult.Error!, finalDiag);

        EmitAudit(input, readAddr, writeCount, final, attempts.Count);

        if (!final.Success && options.ThrowOnFailure)
            throw new Exception(final.Error!.Message);
        return final;
    }

    private static async Task<ReadWriteMultipleResult> DoOneReadWriteMultipleAsync(
        ReadWriteMultipleInput input, WriteOptions options, ConnectionKey key,
        Stopwatch totalSw, ushort readAddr, ushort writeAddr, ushort readCount, ushort writeCount,
        CancellationToken cancellationToken)
    {
        ModbusLease? lease;
        long connectMs = 0;
        var acquireSw = Stopwatch.StartNew();
        try
        {
            lease = await ModbusSession.AcquireAsync(key, options, cancellationToken).ConfigureAwait(false);
            connectMs = lease.ConnectTimeMs;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is TimeoutException or SocketException)
        {
            var category = ex is TimeoutException
                ? (ex.Message.StartsWith("Acquire timed out", StringComparison.Ordinal) ? ErrorCategory.Backpressure : ErrorCategory.Timeout)
                : ErrorCategory.ConnectionRefused;
            var diag = new Diagnostics(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, readAddr, readCount);
            return new ReadWriteMultipleResult(new ErrorDetail(category, true, ex.Message), diag);
        }

        await using (lease)
        {
            using var cancelReg = cancellationToken.Register(() => lease.DisposeUnderlyingSocketForCancellation());
            var opSw = Stopwatch.StartNew();

            try
            {
                ushort[] read = await lease.Master.ReadWriteMultipleRegistersAsync(
                    input.UnitId, readAddr, readCount, writeAddr, input.WriteRegisters!).ConfigureAwait(false);
                var diag = new Diagnostics(connectMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, readAddr, readCount);
                return new ReadWriteMultipleResult(read, diag);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lease.Poison();
                throw;
            }
            catch (SlaveException ex)
            {
                var diag = new Diagnostics(connectMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, readAddr, readCount);
                return new ReadWriteMultipleResult(
                    new ErrorDetail(ErrorCategory.ModbusException, ex.SlaveExceptionCode is 5 or 6 or 10 or 11,
                        ex.Message, modbusExceptionCode: ex.SlaveExceptionCode), diag);
            }
            catch (IOException ex) when (ex.InnerException is SocketException inner)
            {
                lease.Poison();
                var diag = new Diagnostics(connectMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, readAddr, readCount);
                return new ReadWriteMultipleResult(
                    new ErrorDetail(ErrorCategory.SocketError, false, ex.Message,
                        socketErrorCode: inner.SocketErrorCode.ToString()), diag);
            }
            catch (Exception ex)
            {
                lease.Poison();
                var category = ex switch
                {
                    TimeoutException => ErrorCategory.Timeout,
                    SocketException => ErrorCategory.SocketError,
                    _ => ErrorCategory.Unexpected,
                };
                var diag = new Diagnostics(connectMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, readAddr, readCount);
                return new ReadWriteMultipleResult(new ErrorDetail(category, ex is TimeoutException or SocketException, ex.Message), diag);
            }
        }
    }

    private static void EmitAudit(ReadWriteMultipleInput input, ushort readAddr, ushort writeCount,
        ReadWriteMultipleResult result, int attemptCount)
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
            Operation = "ReadWriteMultiple",
            Host = input.Host,
            Port = input.Port,
            UnitId = input.UnitId,
            FunctionCode = 23,
            StartAddress = readAddr,
            Count = writeCount,
            TransportSecurity = TransportSecurity.None,
            Success = result.Success,
            ErrorCategory = result.Error?.Category.ToString(),
            ModbusExceptionCode = result.Error?.ModbusExceptionCode,
            AttemptCount = Math.Max(1, attemptCount),
            TotalTimeMs = result.Diagnostics.TotalTimeMs,
            ValuesWritten = input.WriteRegisters,
        });
    }
}
