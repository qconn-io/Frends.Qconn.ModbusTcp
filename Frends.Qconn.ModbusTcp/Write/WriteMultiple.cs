using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Write.Definitions;

namespace Frends.Qconn.ModbusTcp.Write;

/// <summary>Frends Task for writing multiple coils (FC15) or multiple holding registers (FC16).
/// Dispatch is determined by Input.DataType (Coils → FC15, HoldingRegisters → FC16).</summary>
public static class WriteMultiple
{
    /// <summary>Writes an array of values to contiguous addresses. The encoding is chosen per
    /// Input.DataType and Input.ValueType.</summary>
    /// <param name="input">Connection parameters + values array (Input.Values).
    /// For Coils: bool[]. For HoldingRegisters with numeric ValueType: numeric array.
    /// For HoldingRegisters with AsciiString: string (encoded to Input.NumberOfValues registers, null-padded).</param>
    /// <param name="options">Timeout, pool, retry, breaker, and error-handling options.</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>WriteResult with diagnostics. Throws on failure when ThrowOnFailure is true.</returns>
    public static Task<WriteResult> WriteData(
        [PropertyTab] WriteInput input,
        [PropertyTab] WriteOptions options,
        CancellationToken cancellationToken)
    {
        if (input.Values is null)
            throw new ArgumentException("WriteMultiple requires Input.Values to be set.", nameof(input));

        ushort wireAddr = ModbusDecoder.TranslateAddress(input.StartAddress, options.AddressingMode);

        return input.DataType switch
        {
            ModbusDataType.Coils => WriteCoils(input, options, wireAddr, cancellationToken),
            ModbusDataType.HoldingRegisters => WriteRegisters(input, options, wireAddr, cancellationToken),
            _ => throw new ArgumentException(
                $"WriteMultiple supports DataType Coils or HoldingRegisters; got {input.DataType}.", nameof(input)),
        };
    }

    private static Task<WriteResult> WriteCoils(WriteInput input, WriteOptions options, ushort wireAddr, CancellationToken ct)
    {
        var bools = ModbusEncoder.EncodeBools(input.Values!);
        ushort wireCount = (ushort)bools.Length;

        return WriteExecutor.ExecuteAsync(
            host: input.Host, port: input.Port, unitId: input.UnitId,
            options: options,
            operationName: "WriteMultipleCoils",
            functionCode: 15,
            wireAddr: wireAddr,
            wireCount: wireCount,
            valuesWritten: bools,
            op: master => master.WriteMultipleCoilsAsync(input.UnitId, wireAddr, bools),
            ct);
    }

    private static Task<WriteResult> WriteRegisters(WriteInput input, WriteOptions options, ushort wireAddr, CancellationToken ct)
    {
        ushort[] registers;
        if (input.ValueType == ModbusValueType.AsciiString)
        {
            if (input.Values is not string str)
                throw new ArgumentException("AsciiString WriteMultiple requires Input.Values to be a string.", nameof(input));
            registers = ModbusEncoder.EncodeAsciiString(str, input.NumberOfValues);
        }
        else
        {
            registers = ModbusEncoder.Encode(input.Values!, input.ValueType, options.ByteOrder, options.Scale, options.Offset);
        }

        ushort wireCount = (ushort)registers.Length;
        if (wireCount > 123)
            throw new ArgumentException(
                $"WriteMultipleRegisters maximum is 123 registers per the Modbus spec; got {wireCount}.", nameof(input));

        return WriteExecutor.ExecuteAsync(
            host: input.Host, port: input.Port, unitId: input.UnitId,
            options: options,
            operationName: "WriteMultipleRegisters",
            functionCode: 16,
            wireAddr: wireAddr,
            wireCount: wireCount,
            valuesWritten: input.Values,
            op: master => master.WriteMultipleRegistersAsync(input.UnitId, wireAddr, registers),
            ct);
    }
}
