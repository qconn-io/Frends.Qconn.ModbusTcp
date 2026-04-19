using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.ReadBatch.Definitions;
using NModbus;

namespace Frends.Qconn.ModbusTcp.ReadBatch;

/// <summary>Frends Task for reading multiple register blocks from a Modbus TCP slave over a single TCP connection.</summary>
public static class ReadBatch
{
    /// <summary>
    /// Opens one TCP connection and executes all reads in Input.Items sequentially, then closes.
    /// A Modbus exception on one item fails that item but does not abort the batch.
    /// A socket-level failure aborts the entire batch and sets BatchResult.Success = false.
    /// </summary>
    /// <param name="input">Connection info and list of register reads to perform.</param>
    /// <param name="options">Timeout, byte order, and error-handling options (shared across all items).</param>
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
        TcpClient? tcpClient = null;

        // Connect once
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
            var diag = MakeDiag(connectTimeMs, 0, totalSw.ElapsedMilliseconds, input, 0, 0);
            return new BatchResult(items, diag,
                new ErrorDetail(ErrorCategory.Timeout, true, ex.Message));
        }
        catch (SocketException ex)
        {
            var diag = MakeDiag(connectTimeMs, 0, totalSw.ElapsedMilliseconds, input, 0, 0);
            return new BatchResult(items, diag,
                new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                    socketErrorCode: ex.SocketErrorCode.ToString()));
        }

        using var master = new ModbusFactory().CreateMaster(tcpClient);
        master.Transport.ReadTimeout  = options.ReadTimeoutMs;
        master.Transport.WriteTimeout = options.ReadTimeoutMs;
        using var cancelReg = cancellationToken.Register(() => tcpClient.Dispose());

        // Execute each item
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
                    bool[] coils = await master.ReadCoilsAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                    decoded = coils;
                }
                else if (item.DataType == ModbusDataType.DiscreteInputs)
                {
                    bool[] disc = await master.ReadInputsAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                    decoded = disc;
                }
                else if (item.DataType == ModbusDataType.HoldingRegisters)
                {
                    ushort[] regs = await master.ReadHoldingRegistersAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                    rawRegisters = regs;
                    decoded = ModbusDecoder.Decode(regs, item.ValueType, options.ByteOrder,
                        item.NumberOfValues, item.Scale, item.Offset);
                }
                else // InputRegisters
                {
                    ushort[] regs = await master.ReadInputRegistersAsync(input.UnitId, wireAddr, wireCount).ConfigureAwait(false);
                    rawRegisters = regs;
                    decoded = ModbusDecoder.Decode(regs, item.ValueType, options.ByteOrder,
                        item.NumberOfValues, item.Scale, item.Offset);
                }

                items[item.Name] = new ReadOutcome(decoded, rawRegisters);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (SlaveException ex)
            {
                // Item-level Modbus exception: record failure, continue batch
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
                // Socket-level failure: abort batch
                var diag = MakeDiag(connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                return new BatchResult(items, diag,
                    new ErrorDetail(ErrorCategory.Timeout, true, ex.Message));
            }
            catch (SocketException ex)
            {
                // Socket-level failure: abort batch
                var diag = MakeDiag(connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                return new BatchResult(items, diag,
                    new ErrorDetail(MapSocketCategory(ex), IsTransientSocket(ex), ex.Message,
                        socketErrorCode: ex.SocketErrorCode.ToString()));
            }
            catch (Exception ex)
            {
                // Unexpected: treat as socket-level abort
                var diag = MakeDiag(connectTimeMs, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, input, wireAddr, wireCount);
                return new BatchResult(items, diag,
                    new ErrorDetail(ErrorCategory.Unexpected, false, ex.Message));
            }
        }

        var finalDiag = MakeDiag(connectTimeMs, 0, totalSw.ElapsedMilliseconds, input, 0, 0);
        return new BatchResult(items, finalDiag);
    }

    private static Diagnostics MakeDiag(long connectMs, long readMs, long totalMs,
        BatchInput input, ushort wireAddr, ushort wireCount) =>
        new(connectMs, readMs, totalMs, input.Host, input.Port, input.UnitId, wireAddr, wireCount);

    private static ErrorCategory MapSocketCategory(SocketException ex) =>
        ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused  => ErrorCategory.ConnectionRefused,
            SocketError.HostUnreachable    => ErrorCategory.HostUnreachable,
            SocketError.NetworkUnreachable => ErrorCategory.HostUnreachable,
            _                              => ErrorCategory.Unexpected,
        };

    private static bool IsTransientSocket(SocketException ex) =>
        ex.SocketErrorCode is
            SocketError.ConnectionRefused or
            SocketError.HostUnreachable or
            SocketError.NetworkUnreachable or
            SocketError.TimedOut;

    private static bool IsTransientModbus(int code) => code is 5 or 6 or 10 or 11;
}
