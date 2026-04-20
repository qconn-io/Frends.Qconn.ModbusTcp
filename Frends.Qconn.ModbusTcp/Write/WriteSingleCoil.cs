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
    /// <summary>Writes a single coil value (ON or OFF) to a Modbus TCP slave device.</summary>
    /// <param name="input">Connection parameters and the bool value to write.</param>
    /// <param name="options">Timeout, pool, retry, breaker, and error-handling options.</param>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>WriteResult with diagnostics. Throws on failure when ThrowOnFailure is true (the default for writes).</returns>
    public static Task<WriteResult> WriteData(
        [PropertyTab] WriteSingleCoilInput input,
        [PropertyTab] WriteOptions options,
        CancellationToken cancellationToken)
    {
        ushort wireAddr = ModbusDecoder.TranslateAddress(input.StartAddress, options.AddressingMode);

        return WriteExecutor.ExecuteAsync(
            host: input.Host, port: input.Port, unitId: input.UnitId,
            options: options,
            operationName: "WriteSingleCoil",
            functionCode: 5,
            wireAddr: wireAddr,
            wireCount: 1,
            valuesWritten: input.Value,
            op: master => master.WriteSingleCoilAsync(input.UnitId, wireAddr, input.Value),
            cancellationToken);
    }
}
