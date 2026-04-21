using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Write.Definitions;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Shared execution pipeline for Write Tasks: WriteGuard → breaker → retry loop → pool acquire → op →
/// audit → error mapping → WriteResult. Called by every public Write Task.</summary>
internal static class WriteExecutor
{
    internal static async Task<WriteResult> ExecuteAsync(
        string host, int port, byte unitId,
        WriteOptions options,
        string operationName,
        int functionCode,
        ushort wireAddr,
        ushort wireCount,
        object? valuesWritten,
        Func<IModbusMaster, Task> op,
        CancellationToken cancellationToken)
    {
        WriteGuard.EnsureAllowed(options.AllowWrites);

        var totalSw = Stopwatch.StartNew();
        var key = new ConnectionKey(host, port, unitId,
            options.TransportMode, TransportSecurity.None, null, null);
        var breaker = BreakerRegistry.Get(key, options.CircuitBreaker);

        var attempts = new List<AttemptRecord>();
        int maxAttempts = Math.Max(1, options.Retry.MaxAttempts);
        WriteResult? attemptResult = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.CircuitBreaker.Enabled && !breaker.CanPass(BreakerRegistry.Clock))
            {
                var diag = MakeDiag(0, 0, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount);
                attemptResult = new WriteResult(
                    new ErrorDetail(ErrorCategory.CircuitOpen, true,
                    $"Circuit open for {host}:{port}/UnitId={unitId}."), diag);
                attempts.Add(new AttemptRecord(attempt, 0, ErrorCategory.CircuitOpen, attemptResult.Error!.Message));
                break;
            }

            var attemptSw = Stopwatch.StartNew();
            attemptResult = await DoOneWriteAsync(host, port, unitId, options, key, totalSw,
                wireAddr, wireCount, op, cancellationToken).ConfigureAwait(false);

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
            ? new WriteResult(attemptResult.WireRegistersWritten, finalDiag)
            : new WriteResult(attemptResult.Error!, finalDiag);

        EmitAudit(host, port, unitId, operationName, functionCode, wireAddr, wireCount, valuesWritten, final, attempts.Count);
        return ThrowIfNeeded(final, options);
    }

    private static async Task<WriteResult> DoOneWriteAsync(
        string host, int port, byte unitId,
        WriteOptions options,
        ConnectionKey key,
        Stopwatch totalSw,
        ushort wireAddr, ushort wireCount,
        Func<IModbusMaster, Task> op,
        CancellationToken cancellationToken)
    {
        ModbusLease? lease;
        long connectTimeMs = 0;
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
            return new WriteResult(
                new ErrorDetail(category, true, ex.Message),
                MakeDiag(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount));
        }
        catch (SocketException ex)
        {
            return new WriteResult(
                new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                    socketErrorCode: ex.SocketErrorCode.ToString()),
                MakeDiag(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount));
        }

        await using (lease)
        {
            using var cancelReg = cancellationToken.Register(() => lease.DisposeUnderlyingSocketForCancellation());
            var opSw = Stopwatch.StartNew();

            try
            {
                await op(lease.Master).ConfigureAwait(false);
                return new WriteResult(
                    wireCount,
                    MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount));
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
            catch (TimeoutException ex)
            {
                lease.Poison();
                return new WriteResult(
                    new ErrorDetail(ErrorCategory.Timeout, true, ex.Message),
                    MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount));
            }
            catch (SocketException ex)
            {
                lease.Poison();
                return new WriteResult(
                    new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                        socketErrorCode: ex.SocketErrorCode.ToString()),
                    MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount));
            }
            catch (SlaveException ex)
            {
                return new WriteResult(
                    new ErrorDetail(ErrorCategory.ModbusException, IsTransientModbus(ex.SlaveExceptionCode),
                        ex.Message, modbusExceptionCode: ex.SlaveExceptionCode),
                    MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount));
            }
            catch (IOException ex) when (ex.InnerException is SocketException inner)
            {
                lease.Poison();
                return new WriteResult(
                    new ErrorDetail(MapSocketCategory(inner), IsTransientSocket(inner), ex.Message,
                        socketErrorCode: inner.SocketErrorCode.ToString()),
                    MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount));
            }
            catch (Exception ex)
            {
                lease.Poison();
                return new WriteResult(
                    new ErrorDetail(ErrorCategory.Unexpected, false, ex.Message),
                    MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, host, port, unitId, wireAddr, wireCount));
            }
        }
    }

    private static WriteResult ThrowIfNeeded(WriteResult r, WriteOptions options)
    {
        if (!r.Success && options.ThrowOnFailure)
            throw new Exception(r.Error!.Message);
        return r;
    }

    private static Diagnostics MakeDiag(long connectMs, long opMs, long totalMs,
        string host, int port, byte unitId, ushort wireAddr, ushort wireCount) =>
        new(connectMs, opMs, totalMs, host, port, unitId, wireAddr, wireCount);

    private static void EmitAudit(string host, int port, byte unitId, string operationName, int functionCode,
        ushort wireAddr, ushort wireCount, object? valuesWritten, WriteResult result, int attemptCount)
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
            Operation = operationName,
            Host = host,
            Port = port,
            UnitId = unitId,
            FunctionCode = functionCode,
            StartAddress = wireAddr,
            Count = wireCount,
            TransportSecurity = TransportSecurity.None,
            Success = result.Success,
            ErrorCategory = result.Error?.Category.ToString(),
            ModbusExceptionCode = result.Error?.ModbusExceptionCode,
            AttemptCount = Math.Max(1, attemptCount),
            TotalTimeMs = result.Diagnostics.TotalTimeMs,
            ValuesWritten = valuesWritten,
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

    private static bool IsTransientSocket(SocketException ex) =>
        ex.SocketErrorCode is
            SocketError.ConnectionRefused or
            SocketError.HostUnreachable or
            SocketError.NetworkUnreachable or
            SocketError.TimedOut;

    private static bool IsTransientModbus(int code) => code is 5 or 6 or 10 or 11;
}
