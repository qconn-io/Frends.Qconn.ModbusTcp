namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Byte and word order for multi-register values (Int32, UInt32, Float32, Float64).
/// Letters A-D denote bytes from most-significant (A) to least-significant (D) of the decoded value.</summary>
public enum ByteOrder
{
    /// <summary>ABCD: high word first, high byte first. Modbus spec default. Most PLCs and modern devices.</summary>
    BigEndian,

    /// <summary>DCBA: low word first, low byte first. Full little-endian byte order.</summary>
    LittleEndian,

    /// <summary>BADC: high word first, bytes swapped within each 16-bit word.</summary>
    BigEndianByteSwap,

    /// <summary>CDAB: low word first, high byte first within each 16-bit word.
    /// Modicon/Schneider convention; also common on many energy meters (WattNode, etc.).</summary>
    LittleEndianWordSwap,
}
