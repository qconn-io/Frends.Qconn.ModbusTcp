using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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
    /// <param name="options">Timeout, byte order, scale, and error-handling options.</param>
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
                0, 0, totalSw.ElapsedMilliseconds, input, 0, 0);

        bool isCoilType = input.DataType is ModbusDataType.Coils or ModbusDataType.DiscreteInputs;
        ushort wireCount = isCoilType
            ? input.NumberOfValues
            : ModbusDecoder.ComputeRegisterCount(input.ValueType, input.NumberOfValues);

        int maxCount = isCoilType ? 2000 : 125;
        if (wireCount > maxCount)
            return Fail(
                new ErrorDetail(ErrorCategory.InvalidInput, false,
                    $"Requested {wireCount} registers/coils but maximum for this function code is {maxCount}."),
                0, 0, totalSw.ElapsedMilliseconds, input, 0, wireCount);

        // 2. Translate address
        ushort wireAddr = ModbusDecoder.TranslateAddress(input.StartAddress, options.AddressingMode);

        // 3. Connect with timeout
        long connectTimeMs = 0;
        TcpClient? tcpClient = null;
        try
        {
            (tcpClient, connectTimeMs) = await ModbusConnection
                .ConnectAsync(input.Host, input.Port, options.ConnectTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            return Fail(new ErrorDetail(ErrorCategory.Timeout, true, ex.Message),
                connectTimeMs, 0, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
        }
        catch (SocketException ex)
        {
            return Fail(new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                    socketErrorCode: ex.SocketErrorCode.ToString()),
                connectTimeMs, 0, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
        }

        // 4. Create master, wire up cancellation (NModbus async methods do not accept CancellationToken)
        using var master = new ModbusFactory().CreateMaster(tcpClient);
        master.Transport.ReadTimeout  = options.ReadTimeoutMs;
        master.Transport.WriteTimeout = options.ReadTimeoutMs;

        // Disposing tcpClient unblocks a pending NModbus read when the token fires.
        using var cancelReg = cancellationToken.Register(() => tcpClient.Dispose());

        var readSw = Stopwatch.StartNew();
        try
        {
            object decoded;
            ushort[]? rawRegisters = null;

            // 5. Read by DataType
            if (input.DataType == ModbusDataType.Coils)
            {
                bool[] coils = await master.ReadCoilsAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                decoded = coils;
            }
            else if (input.DataType == ModbusDataType.DiscreteInputs)
            {
                bool[] inputs = await master.ReadInputsAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                decoded = inputs;
            }
            else if (input.DataType == ModbusDataType.HoldingRegisters)
            {
                ushort[] regs = await master.ReadHoldingRegistersAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                rawRegisters = regs;
                decoded = ModbusDecoder.Decode(regs, input.ValueType, options.ByteOrder,
                    input.NumberOfValues, options.Scale, options.Offset);
            }
            else // InputRegisters
            {
                ushort[] regs = await master.ReadInputRegistersAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                rawRegisters = regs;
                decoded = ModbusDecoder.Decode(regs, input.ValueType, options.ByteOrder,
                    input.NumberOfValues, options.Scale, options.Offset);
            }

            long readTimeMs = readSw.ElapsedMilliseconds;
            var diagnostics = new Diagnostics(
                connectTimeMs, readTimeMs, totalSw.ElapsedMilliseconds,
                input.Host, input.Port, input.UnitId, wireAddr, wireCount);

            return new Result(decoded, rawRegisters, diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // NModbus surfaces as SocketException (not ObjectDisposedException) when the socket is disposed
            // via the cancel registration. Catch any exception when cancelled and re-throw as OCE.
            throw new OperationCanceledException(cancellationToken);
        }
        catch (TimeoutException ex)
        {
            var err = new ErrorDetail(ErrorCategory.Timeout, true, ex.Message);
            var r = Fail(err, connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
            if (options.ThrowOnFailure) throw new Exception(ex.Message, ex);
            return r;
        }
        catch (SocketException ex)
        {
            var err = new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                socketErrorCode: ex.SocketErrorCode.ToString());
            var r = Fail(err, connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
            if (options.ThrowOnFailure) throw new Exception(ex.Message, ex);
            return r;
        }
        catch (SlaveException ex)
        {
            var err = new ErrorDetail(ErrorCategory.ModbusException, IsTransientModbus(ex.SlaveExceptionCode),
                ex.Message, modbusExceptionCode: ex.SlaveExceptionCode);
            var r = Fail(err, connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
            if (options.ThrowOnFailure) throw new Exception(ex.Message, ex);
            return r;
        }
        catch (FormatException ex)
        {
            var err = new ErrorDetail(ErrorCategory.DecodingError, false, ex.Message);
            var r = Fail(err, connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
            if (options.ThrowOnFailure) throw new Exception(ex.Message, ex);
            return r;
        }
        catch (Exception ex)
        {
            var err = new ErrorDetail(ErrorCategory.Unexpected, false, ex.Message);
            var r = Fail(err, connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
            if (options.ThrowOnFailure) throw;
            return r;
        }
    }

    private static Result Fail(
        ErrorDetail error, long connectMs, long readMs, long totalMs,
        Input input, ushort wireAddr, ushort wireCount)
    {
        var diagnostics = new Diagnostics(connectMs, readMs, totalMs,
            input.Host, input.Port, input.UnitId, wireAddr, wireCount);
        return new Result(error, diagnostics);
    }

    private static ErrorCategory MapSocketCategory(SocketException ex) =>
        ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => ErrorCategory.ConnectionRefused,
            SocketError.HostUnreachable   => ErrorCategory.HostUnreachable,
            SocketError.NetworkUnreachable => ErrorCategory.HostUnreachable,
            _                             => ErrorCategory.Unexpected,
        };

    private static bool IsTransientSocket(SocketException ex) =>
        ex.SocketErrorCode is
            SocketError.ConnectionRefused or
            SocketError.HostUnreachable or
            SocketError.NetworkUnreachable or
            SocketError.TimedOut;

    // Modbus exception codes: 5=Acknowledge, 6=SlaveDeviceBusy, 10=GatewayPathUnavailable, 11=GatewayTargetDeviceFailedToRespond
    private static bool IsTransientModbus(int code) => code is 5 or 6 or 10 or 11;
}
