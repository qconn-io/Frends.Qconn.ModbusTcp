using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Write.Definitions;

namespace Frends.Qconn.ModbusTcp.Write;

/// <summary>Frends Task for writing a single 16-bit holding register (Modbus FC06).</summary>
public static class WriteSingleRegister
{
    /// <summary>Writes a single register. ValueType must be Raw, UInt16, or Int16.
    /// For multi-register types (Float32, Int32, etc.) use WriteMultiple instead.</summary>
    /// <param name="input">Connection parameters, ValueType, and the value to write.</param>
    /// <param name="options">Timeout, byte order, scale/offset, pool, retry, breaker, and error-handling options.</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>WriteResult with diagnostics. Throws on failure when ThrowOnFailure is true (the default for writes).</returns>
    public static Task<WriteResult> WriteData(
        [PropertyTab] WriteSingleRegisterInput input,
        [PropertyTab] WriteOptions options,
        CancellationToken cancellationToken)
    {
        if (input.Value is null)
            throw new ArgumentException("WriteSingleRegister requires Input.Value to be set.", nameof(input));

        if (input.ValueType is not (ModbusValueType.Raw or ModbusValueType.UInt16 or ModbusValueType.Int16))
            throw new ArgumentException(
                $"WriteSingleRegister supports ValueType Raw, UInt16, or Int16; got {input.ValueType}. " +
                "For multi-register types use WriteMultiple.", nameof(input));

        ushort wireAddr = ModbusDecoder.TranslateAddress(input.StartAddress, options.AddressingMode);

        object valueArray = input.Value is Array ? input.Value : new[] { input.Value };
        var encoded = ModbusEncoder.Encode(valueArray, input.ValueType, options.ByteOrder, options.Scale, options.Offset);
        if (encoded.Length != 1)
            throw new ArgumentException($"WriteSingleRegister encodes to 1 register; got {encoded.Length}.", nameof(input));
        ushort wireValue = encoded[0];

        return WriteExecutor.ExecuteAsync(
            host: input.Host, port: input.Port, unitId: input.UnitId,
            options: options,
            operationName: "WriteSingleRegister",
            functionCode: 6,
            wireAddr: wireAddr,
            wireCount: 1,
            valuesWritten: input.Value,
            op: master => master.WriteSingleRegisterAsync(input.UnitId, wireAddr, wireValue),
            cancellationToken);
    }
}
