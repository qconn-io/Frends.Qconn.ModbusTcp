using System;
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

/// <summary>Frends Task for Modbus FC23 (Read/Write Multiple Registers): atomic write+read in one request.</summary>
public static class ReadWriteMultiple
{
    /// <summary>Executes a Modbus FC23 request. Writes are applied first, then the read block is returned.
    /// Requires ModbusWritesAllowed to be absent or 'true'.</summary>
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

        if (options.CircuitBreaker.Enabled && !breaker.CanPass(BreakerRegistry.Clock))
        {
            var diag = new Diagnostics(0, 0, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, readAddr, readCount);
            var r = new ReadWriteMultipleResult(new ErrorDetail(ErrorCategory.CircuitOpen, true,
                $"Circuit open for {input.Host}:{input.Port}/UnitId={input.UnitId}."), diag);
            EmitAudit(input, readAddr, writeCount, r);
            if (options.ThrowOnFailure) throw new Exception(r.Error!.Message);
            return r;
        }

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
            breaker.RecordFailure(BreakerRegistry.Clock);
            var category = ex is TimeoutException
                ? (ex.Message.StartsWith("Acquire timed out", StringComparison.Ordinal) ? ErrorCategory.Backpressure : ErrorCategory.Timeout)
                : ErrorCategory.ConnectionRefused;
            var diag = new Diagnostics(acquireSw.ElapsedMilliseconds, 0, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, readAddr, readCount);
            var r = new ReadWriteMultipleResult(new ErrorDetail(category, true, ex.Message), diag);
            EmitAudit(input, readAddr, writeCount, r);
            if (options.ThrowOnFailure) throw new Exception(r.Error!.Message);
            return r;
        }

        await using (lease)
        {
            using var cancelReg = cancellationToken.Register(() => lease.DisposeUnderlyingSocketForCancellation());
            var opSw = Stopwatch.StartNew();

            try
            {
                ushort[] read = await lease.Master.ReadWriteMultipleRegistersAsync(
                    input.UnitId, readAddr, readCount, writeAddr, input.WriteRegisters).ConfigureAwait(false);
                breaker.RecordSuccess();
                var diag = new Diagnostics(connectMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, readAddr, readCount);
                var r = new ReadWriteMultipleResult(read, diag);
                EmitAudit(input, readAddr, writeCount, r);
                return r;
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
                var diag = new Diagnostics(connectMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, readAddr, readCount);
                var r = new ReadWriteMultipleResult(
                    new ErrorDetail(ErrorCategory.ModbusException, ex.SlaveExceptionCode is 5 or 6 or 10 or 11,
                        ex.Message, modbusExceptionCode: ex.SlaveExceptionCode), diag);
                EmitAudit(input, readAddr, writeCount, r);
                if (options.ThrowOnFailure) throw new Exception(r.Error!.Message);
                return r;
            }
            catch (Exception ex)
            {
                lease.Poison();
                if (ex is TimeoutException or SocketException)
                    breaker.RecordFailure(BreakerRegistry.Clock);
                var diag = new Diagnostics(connectMs, opSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds,
                    input.Host, input.Port, input.UnitId, readAddr, readCount);
                var category = ex switch
                {
                    TimeoutException => ErrorCategory.Timeout,
                    SocketException => ErrorCategory.SocketError,
                    _ => ErrorCategory.Unexpected,
                };
                var r = new ReadWriteMultipleResult(new ErrorDetail(category, ex is TimeoutException or SocketException, ex.Message), diag);
                EmitAudit(input, readAddr, writeCount, r);
                if (options.ThrowOnFailure) throw;
                return r;
            }
        }
    }

    private static void EmitAudit(ReadWriteMultipleInput input, ushort readAddr, ushort writeCount, ReadWriteMultipleResult result)
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
            AttemptCount = 1,
            TotalTimeMs = result.Diagnostics.TotalTimeMs,
            ValuesWritten = input.WriteRegisters,
        });
    }
}
