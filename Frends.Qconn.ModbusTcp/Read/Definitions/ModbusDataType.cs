namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Modbus data type (function code selection).</summary>
public enum ModbusDataType
{
    /// <summary>Coil outputs (FC01). Single-bit, read-write. Returns bool[].</summary>
    Coils,

    /// <summary>Discrete inputs (FC02). Single-bit, read-only. Returns bool[].</summary>
    DiscreteInputs,

    /// <summary>Holding registers (FC03). 16-bit, read-write. Most common.</summary>
    HoldingRegisters,

    /// <summary>Input registers (FC04). 16-bit, read-only.</summary>
    InputRegisters,
}
