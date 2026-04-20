using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Result of a ReadWriteMultiple (FC23) operation.</summary>
public class ReadWriteMultipleResult
{
    /// <summary>True if both the write and read portions completed without error.</summary>
    public bool Success { get; }

    /// <summary>Raw 16-bit register values read back from the read block. Null on failure.</summary>
    public ushort[]? ReadRegisters { get; }

    /// <summary>Structured error information when Success is false; null when Success is true.</summary>
    public ErrorDetail? Error { get; }

    /// <summary>Timing and connection metrics. Always populated.</summary>
    public Diagnostics Diagnostics { get; }

    public ReadWriteMultipleResult(ushort[] readRegisters, Diagnostics diagnostics)
    {
        Success = true;
        ReadRegisters = readRegisters;
        Diagnostics = diagnostics;
        Error = null;
    }

    public ReadWriteMultipleResult(ErrorDetail error, Diagnostics diagnostics)
    {
        Success = false;
        ReadRegisters = null;
        Error = error;
        Diagnostics = diagnostics;
    }
}
