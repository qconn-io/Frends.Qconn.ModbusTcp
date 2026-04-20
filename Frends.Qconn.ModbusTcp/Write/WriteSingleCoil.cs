using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Write.Definitions;

namespace Frends.Qconn.ModbusTcp.Write;

/// <summary>Frends Task for writing a single coil (Modbus FC05).</summary>
public static class WriteSingleCoil
{
    /// <summary>Writes a single coil value. Input.Values must be a bool.
    /// Requires Environment Variable ModbusWritesAllowed to be absent or 'true' (see SECURITY.md).</summary>
    /// <param name="input">Connection parameters + bool value to write (via Input.Values).</param>
    /// <param name="options">Timeout, pool, retry, breaker, and error-handling options.
    /// Note: WriteOptions overrides ThrowOnFailure default to true for writes.</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>WriteResult with diagnostics. Throws on failure when ThrowOnFailure is true (the default for writes).</returns>
    public static Task<WriteResult> WriteData(
        [PropertyTab] WriteInput input,
        [PropertyTab] WriteOptions options,
        CancellationToken cancellationToken)
    {
        if (input.Values is not bool boolValue)
            throw new ArgumentException("WriteSingleCoil requires Input.Values to be a bool.", nameof(input));

        ushort wireAddr = ModbusDecoder.TranslateAddress(input.StartAddress, options.AddressingMode);

        return WriteExecutor.ExecuteAsync(input, options,
            operationName: "WriteSingleCoil",
            functionCode: 5,
            wireAddr: wireAddr,
            wireCount: 1,
            valuesWritten: boolValue,
            op: master => master.WriteSingleCoilAsync(input.UnitId, wireAddr, boolValue),
            cancellationToken);
    }
}
