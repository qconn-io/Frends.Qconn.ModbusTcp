namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>How to interpret the raw 16-bit register words returned from the device.</summary>
public enum ModbusValueType
{
    /// <summary>Raw 16-bit values. Returns ushort[] for register types, bool[] for coil types.
    /// Use when you need the raw wire data (e.g. to pass through to a downstream system unchanged).</summary>
    Raw,

    /// <summary>Signed 16-bit integer. One register per value.</summary>
    Int16,

    /// <summary>Unsigned 16-bit integer. One register per value.</summary>
    UInt16,

    /// <summary>Signed 32-bit integer. Two registers per value. Byte/word order from Options.ByteOrder.</summary>
    Int32,

    /// <summary>Unsigned 32-bit integer. Two registers per value. Byte/word order from Options.ByteOrder.</summary>
    UInt32,

    /// <summary>IEEE-754 single-precision float. Two registers per value. Byte/word order from Options.ByteOrder.</summary>
    Float32,

    /// <summary>IEEE-754 double-precision float. Four registers per value. Byte/word order from Options.ByteOrder.</summary>
    Float64,

    /// <summary>ASCII string. NumberOfValues is interpreted as number of registers (2 chars each); returns a single
    /// string with null-padding trimmed.</summary>
    AsciiString,
}
