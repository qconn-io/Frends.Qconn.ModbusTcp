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
using NModbus;

namespace Frends.Qconn.ModbusTcp.Read;

/// <summary>Frends Task for reading typed, scaled values from Modbus TCP slave devices.</summary>
public static class Read
{
    /// <summary>
    /// Reads one or more values from a Modbus TCP slave device and returns typed, endianness-corrected,
    /// scale-adjusted output. Supports coils, discrete inputs, holding registers, and input registers.
    /// Returns a structured Result — check Result.Success before using Result.Data.
    /// </summary>
    /// <param name="input">Connection and register parameters.</param>
    /// <param name="options">Timeout, byte order, scale, pool, retry, breaker, and error-handling options.</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent; propagates correctly on Process abort.</param>
    /// <returns>Result containing typed data, raw registers for debugging, error details, and timing diagnostics.</returns>
    public static async Task<Result> ReadData(
        [PropertyTab] Input input,
        [PropertyTab] Options options,
        CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();

        // 1. Validate input
        if (input.NumberOfValues == 0)
            return Fail(
                new ErrorDetail(ErrorCategory.InvalidInput, false, "NumberOfValues must be greater than 0."),
                0, 0, totalSw.ElapsedMilliseconds, input, 0, 0, 1);

        bool isCoilType = input.DataType is ModbusDataType.Coils or ModbusDataType.DiscreteInputs;
        ushort wireCount = isCoilType
            ? input.NumberOfValues
            : ModbusDecoder.ComputeRegisterCount(input.ValueType, input.NumberOfValues);

        int maxCount = isCoilType ? 2000 : 125;
        if (wireCount > maxCount)
            return Fail(
                new ErrorDetail(ErrorCategory.InvalidInput, false,
                    $"Requested {wireCount} registers/coils but maximum for this function code is {maxCount}."),
                0, 0, totalSw.ElapsedMilliseconds, input, 0, wireCount, 1);

        ushort wireAddr = ModbusDecoder.TranslateAddress(input.StartAddress, options.AddressingMode);

        var key = new ConnectionKey(input.Host, input.Port, input.UnitId,
            options.TransportMode, TransportSecurity.None, null, null);

        // 2. Retry + breaker loop
        var attempts = new List<AttemptRecord>();
        var breaker = BreakerRegistry.Get(key, options.CircuitBreaker);
        int maxAttempts = Math.Max(1, options.Retry.MaxAttempts);
        Result? attemptResult = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Breaker short-circuit
            if (options.CircuitBreaker.Enabled && !breaker.CanPass(BreakerRegistry.Clock))
            {
                attemptResult = Fail(
                    new ErrorDetail(ErrorCategory.CircuitOpen, true,
                        $"Circuit open for {input.Host}:{input.Port}/UnitId={input.UnitId}."),
                    0, 0, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount, attempt);
                attempts.Add(new AttemptRecord(attempt, 0, ErrorCategory.CircuitOpen,
                    attemptResult.Error!.Message));
                break;
            }

            var attemptSw = Stopwatch.StartNew();
            attemptResult = await DoOneReadAsync(input, options, key, totalSw, wireAddr, wireCount, cancellationToken)
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

            if (attempt >= maxAttempts ||
                !RetryExecutor.ShouldRetry(category, modbusCode, options.Retry))
                break;

            await RetryExecutor.DelayAsync(
                RetryExecutor.ComputeBackoff(attempt, options.Retry), cancellationToken)
                .ConfigureAwait(false);
        }

        // 3. Attach attempt history to final Diagnostics (only if retries happened)
        var final = AttachHistory(attemptResult!, attempts, totalSw.ElapsedMilliseconds);

        // 4. Audit emit — always fires (success or failure), never redacted.
        EmitAudit(input, options, wireAddr, wireCount, final, attempts.Count);

        if (!final.Success && options.ThrowOnFailure && final.Error!.Category != ErrorCategory.InvalidInput)
            throw new Exception(final.Error.Message);

        return final;
    }

    private static void EmitAudit(Input input, Options options, ushort wireAddr, ushort wireCount,
        Result result, int attemptCount)
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
            Operation = "Read",
            Host = input.Host,
            Port = input.Port,
            UnitId = input.UnitId,
            FunctionCode = FunctionCodeFor(input.DataType),
            StartAddress = wireAddr,
            Count = wireCount,
            TransportSecurity = TransportSecurity.None,
            Success = result.Success,
            ErrorCategory = result.Error?.Category.ToString(),
            ModbusExceptionCode = result.Error?.ModbusExceptionCode,
            AttemptCount = Math.Max(1, attemptCount),
            TotalTimeMs = result.Diagnostics.TotalTimeMs,
            ValuesWritten = null,
        });
    }

    private static int FunctionCodeFor(ModbusDataType t) => t switch
    {
        ModbusDataType.Coils => 1,
        ModbusDataType.DiscreteInputs => 2,
        ModbusDataType.HoldingRegisters => 3,
        ModbusDataType.InputRegisters => 4,
        _ => 0,
    };

    /// <summary>Executes one read attempt end-to-end (acquire → read → convert). Returns a Result,
    /// never throws except for OperationCanceledException.</summary>
    private static async Task<Result> DoOneReadAsync(
        Input input, Options options, ConnectionKey key,
        Stopwatch totalSw, ushort wireAddr, ushort wireCount,
        CancellationToken cancellationToken)
    {
        ModbusLease? lease;
        long connectTimeMs;
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
            // TimeoutException from the pool path may be either "connect" or "acquire backpressure" —
            // distinguish by the message prefix from ConnectionPool.
            var category = ex.Message.StartsWith("Acquire timed out", StringComparison.Ordinal)
                ? ErrorCategory.Backpressure : ErrorCategory.Timeout;
            return Fail(new ErrorDetail(category, true, ex.Message),
                acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount, 1);
        }
        catch (SocketException ex)
        {
            return Fail(new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                    socketErrorCode: ex.SocketErrorCode.ToString()),
                acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount, 1);
        }

        await using (lease)
        {
            using var cancelReg = cancellationToken.Register(() => lease.DisposeUnderlyingSocketForCancellation());

            var readSw = Stopwatch.StartNew();
            try
            {
                object decoded;
                ushort[]? rawRegisters = null;

                if (input.DataType == ModbusDataType.Coils)
                {
                    bool[] coils = await lease.Master.ReadCoilsAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                    decoded = coils;
                }
                else if (input.DataType == ModbusDataType.DiscreteInputs)
                {
                    bool[] inputs = await lease.Master.ReadInputsAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                    decoded = inputs;
                }
                else if (input.DataType == ModbusDataType.HoldingRegisters)
                {
                    ushort[] regs = await lease.Master.ReadHoldingRegistersAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                    rawRegisters = regs;
                    decoded = ModbusDecoder.Decode(regs, input.ValueType, options.ByteOrder,
                        input.NumberOfValues, options.Scale, options.Offset);
                }
                else
                {
                    ushort[] regs = await lease.Master.ReadInputRegistersAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                    rawRegisters = regs;
                    decoded = ModbusDecoder.Decode(regs, input.ValueType, options.ByteOrder,
                        input.NumberOfValues, options.Scale, options.Offset);
                }

                var diagnostics = new Diagnostics(
                    connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, wireAddr, wireCount);
                return new Result(decoded, rawRegisters, diagnostics);
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
                return Fail(new ErrorDetail(ErrorCategory.Timeout, true, ex.Message),
                    connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount, 1);
            }
            catch (SocketException ex)
            {
                lease.Poison();
                return Fail(new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                        socketErrorCode: ex.SocketErrorCode.ToString()),
                    connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount, 1);
            }
            catch (SlaveException ex)
            {
                return Fail(new ErrorDetail(ErrorCategory.ModbusException, IsTransientModbus(ex.SlaveExceptionCode),
                        ex.Message, modbusExceptionCode: ex.SlaveExceptionCode),
                    connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount, 1);
            }
            catch (FormatException ex)
            {
                return Fail(new ErrorDetail(ErrorCategory.DecodingError, false, ex.Message),
                    connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount, 1);
            }
            catch (Exception ex)
            {
                lease.Poison();
                return Fail(new ErrorDetail(ErrorCategory.Unexpected, false, ex.Message),
                    connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount, 1);
            }
        }
    }

    private static Result Fail(
        ErrorDetail error, long connectMs, long readMs, long totalMs,
        Input input, ushort wireAddr, ushort wireCount, int attemptCount)
    {
        var diagnostics = new Diagnostics(connectMs, readMs, totalMs,
            input.Host, input.Port, input.UnitId, wireAddr, wireCount, attemptCount);
        return new Result(error, diagnostics);
    }

    private static Result AttachHistory(Result r, List<AttemptRecord> attempts, long totalMs)
    {
        // Only attach history when more than one attempt ran (keeps v1 Diagnostics shape for default single-shot).
        if (attempts.Count <= 1) return r;

        var d = r.Diagnostics;
        var newDiag = new Diagnostics(
            d.ConnectTimeMs, d.ReadTimeMs, totalMs,
            d.Host, d.Port, d.UnitId, d.WireStartAddress, d.WireRegisterCount,
            attempts.Count)
        {
            AttemptHistory = attempts.AsReadOnly(),
        };
        return r.Success
            ? new Result(r.Data!, r.RawRegisters, newDiag)
            : new Result(r.Error!, newDiag);
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
