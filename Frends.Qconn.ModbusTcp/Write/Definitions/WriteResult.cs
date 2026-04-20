using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Result of a Modbus TCP write operation. Check Success before interpreting.</summary>
public class WriteResult
{
    /// <summary>True if the write completed without error.</summary>
    public bool Success { get; }

    /// <summary>Number of 16-bit registers (or coils) written on the wire.
    /// Zero for failures that never hit the wire.</summary>
    public ushort WireRegistersWritten { get; }

    /// <summary>Structured error information when Success is false; null when Success is true.</summary>
    public ErrorDetail? Error { get; }

    /// <summary>Timing and connection metrics. Always populated.</summary>
    public Diagnostics Diagnostics { get; }

    /// <summary>Initializes a successful WriteResult.</summary>
    public WriteResult(ushort wireRegistersWritten, Diagnostics diagnostics)
    {
        Success = true;
        WireRegistersWritten = wireRegistersWritten;
        Diagnostics = diagnostics;
        Error = null;
    }

    /// <summary>Initializes a failed WriteResult.</summary>
    public WriteResult(ErrorDetail error, Diagnostics diagnostics)
    {
        Success = false;
        WireRegistersWritten = 0;
        Error = error;
        Diagnostics = diagnostics;
    }
}
