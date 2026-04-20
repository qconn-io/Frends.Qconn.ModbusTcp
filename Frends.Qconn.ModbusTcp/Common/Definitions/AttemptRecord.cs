using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Common.Definitions;

/// <summary>One row in the retry history attached to Diagnostics.AttemptHistory.
/// Populated on every attempt (success or failure) to aid debugging of intermittent device behavior.</summary>
public sealed class AttemptRecord
{
    /// <summary>1-based attempt number.</summary>
    public int Attempt { get; }

    /// <summary>Wall-clock elapsed time for this attempt, in milliseconds.</summary>
    public long ElapsedMs { get; }

    /// <summary>Result category. ErrorCategory.None on the successful attempt.</summary>
    public ErrorCategory Category { get; }

    /// <summary>Error message on failure; null on success.</summary>
    public string? Message { get; }

    /// <summary>Modbus exception code if the failure was a Modbus-level exception; null otherwise.</summary>
    public int? ModbusExceptionCode { get; }

    public AttemptRecord(int attempt, long elapsedMs, ErrorCategory category, string? message, int? modbusExceptionCode = null)
    {
        Attempt = attempt;
        ElapsedMs = elapsedMs;
        Category = category;
        Message = message;
        ModbusExceptionCode = modbusExceptionCode;
    }
}
