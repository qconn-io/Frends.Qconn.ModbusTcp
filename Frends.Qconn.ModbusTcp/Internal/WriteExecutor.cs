using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Write.Definitions;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Shared execution pipeline for Write Tasks: WriteGuard → breaker → pool acquire → op →
/// audit → error mapping → WriteResult. Called by every public Write Task.</summary>
internal static class WriteExecutor
{
    internal static async Task<WriteResult> ExecuteAsync(
        WriteInput input,
        Options options,
        string operationName,
        int functionCode,
        ushort wireAddr,
        ushort wireCount,
        object? valuesWritten,
        Func<IModbusMaster, Task> op,
        CancellationToken cancellationToken)
    {
        WriteGuard.EnsureAllowed();

        var totalSw = Stopwatch.StartNew();

        var key = new ConnectionKey(input.Host, input.Port, input.UnitId,
            options.TransportMode, TransportSecurity.None, null, null);
        var breaker = BreakerRegistry.Get(key, options.CircuitBreaker);

        if (options.CircuitBreaker.Enabled && !breaker.CanPass(BreakerRegistry.Clock))
        {
            var diag = MakeDiag(0, 0, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
            var r = new WriteResult(new ErrorDetail(ErrorCategory.CircuitOpen, true,
                $"Circuit open for {input.Host}:{input.Port}/UnitId={input.UnitId}."), diag);
            EmitAudit(input, operationName, functionCode, wireAddr, wireCount, valuesWritten, r);
            return ThrowIfNeeded(r, options);
        }

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
            breaker.RecordFailure(BreakerRegistry.Clock);
            var diag = MakeDiag(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
            var r = new WriteResult(new ErrorDetail(category, true, ex.Message), diag);
            EmitAudit(input, operationName, functionCode, wireAddr, wireCount, valuesWritten, r);
            return ThrowIfNeeded(r, options);
        }
        catch (SocketException ex)
        {
            breaker.RecordFailure(BreakerRegistry.Clock);
            var diag = MakeDiag(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
            var r = new WriteResult(new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                socketErrorCode: ex.SocketErrorCode.ToString()), diag);
            EmitAudit(input, operationName, functionCode, wireAddr, wireCount, valuesWritten, r);
            return ThrowIfNeeded(r, options);
        }

        await using (lease)
        {
            using var cancelReg = cancellationToken.Register(() => lease.DisposeUnderlyingSocketForCancellation());
            var opSw = Stopwatch.StartNew();

            try
            {
                await op(lease.Master).ConfigureAwait(false);
                breaker.RecordSuccess();
                var diag = MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                var r = new WriteResult(wireCount, diag);
                EmitAudit(input, operationName, functionCode, wireAddr, wireCount, valuesWritten, r);
                return r;
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
                breaker.RecordFailure(BreakerRegistry.Clock);
                var diag = MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                var r = new WriteResult(new ErrorDetail(ErrorCategory.Timeout, true, ex.Message), diag);
                EmitAudit(input, operationName, functionCode, wireAddr, wireCount, valuesWritten, r);
                return ThrowIfNeeded(r, options);
            }
            catch (SocketException ex)
            {
                lease.Poison();
                breaker.RecordFailure(BreakerRegistry.Clock);
                var diag = MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                var r = new WriteResult(new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                    socketErrorCode: ex.SocketErrorCode.ToString()), diag);
                EmitAudit(input, operationName, functionCode, wireAddr, wireCount, valuesWritten, r);
                return ThrowIfNeeded(r, options);
            }
            catch (SlaveException ex)
            {
                if (CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, ex.SlaveExceptionCode))
                    breaker.RecordFailure(BreakerRegistry.Clock);
                var diag = MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                var r = new WriteResult(new ErrorDetail(ErrorCategory.ModbusException, IsTransientModbus(ex.SlaveExceptionCode),
                    ex.Message, modbusExceptionCode: ex.SlaveExceptionCode), diag);
                EmitAudit(input, operationName, functionCode, wireAddr, wireCount, valuesWritten, r);
                return ThrowIfNeeded(r, options);
            }
            catch (Exception ex)
            {
                lease.Poison();
                var diag = MakeDiag(connectTimeMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                var r = new WriteResult(new ErrorDetail(ErrorCategory.Unexpected, false, ex.Message), diag);
                EmitAudit(input, operationName, functionCode, wireAddr, wireCount, valuesWritten, r);
                return ThrowIfNeeded(r, options);
            }
        }
    }

    private static WriteResult ThrowIfNeeded(WriteResult r, Options options)
    {
        if (!r.Success && options.ThrowOnFailure)
            throw new Exception(r.Error!.Message);
        return r;
    }

    private static Diagnostics MakeDiag(long connectMs, long opMs, long totalMs,
        WriteInput input, ushort wireAddr, ushort wireCount) =>
        new(connectMs, opMs, totalMs, input.Host, input.Port, input.UnitId, wireAddr, wireCount);

    private static void EmitAudit(WriteInput input, string operationName, int functionCode,
        ushort wireAddr, ushort wireCount, object? valuesWritten, WriteResult result)
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
            Host = input.Host,
            Port = input.Port,
            UnitId = input.UnitId,
            FunctionCode = functionCode,
            StartAddress = wireAddr,
            Count = wireCount,
            TransportSecurity = TransportSecurity.None,
            Success = result.Success,
            ErrorCategory = result.Error?.Category.ToString(),
            ModbusExceptionCode = result.Error?.ModbusExceptionCode,
            AttemptCount = 1,
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
