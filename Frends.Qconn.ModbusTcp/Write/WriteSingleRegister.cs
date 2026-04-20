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
    /// <summary>Writes a single register. Input.Values must be a scalar numeric value that fits in a 16-bit register
    /// (UInt16 or Int16 — encoding applies Scale/Offset inversely).</summary>
    /// <param name="input">Connection parameters + value to write. Input.ValueType should be UInt16 or Int16.</param>
    /// <param name="options">Timeout, pool, retry, breaker, and error-handling options.</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>WriteResult with diagnostics. Throws on failure when ThrowOnFailure is true (the default for writes).</returns>
    public static Task<WriteResult> WriteData(
        [PropertyTab] WriteInput input,
        [PropertyTab] WriteOptions options,
        CancellationToken cancellationToken)
    {
        if (input.Values is null)
            throw new ArgumentException("WriteSingleRegister requires Input.Values to be set.", nameof(input));

        if (input.ValueType is not (ModbusValueType.Raw or ModbusValueType.UInt16 or ModbusValueType.Int16))
            throw new ArgumentException(
                $"WriteSingleRegister supports ValueType Raw, UInt16, or Int16; got {input.ValueType}. " +
                "For multi-register types use WriteMultiple.", nameof(input));

        ushort wireAddr = ModbusDecoder.TranslateAddress(input.StartAddress, options.AddressingMode);

        // Wrap scalar as a single-element array for the encoder.
        object valueArray = input.Values is Array ? input.Values : new[] { input.Values };
        var encoded = ModbusEncoder.Encode(valueArray, input.ValueType, options.ByteOrder, options.Scale, options.Offset);
        if (encoded.Length != 1)
            throw new ArgumentException($"WriteSingleRegister encodes to 1 register; got {encoded.Length}.", nameof(input));
        ushort wireValue = encoded[0];

        return WriteExecutor.ExecuteAsync(input, options,
            operationName: "WriteSingleRegister",
            functionCode: 6,
            wireAddr: wireAddr,
            wireCount: 1,
            valuesWritten: input.Values,
            op: master => master.WriteSingleRegisterAsync(input.UnitId, wireAddr, wireValue),
            cancellationToken);
    }
}
