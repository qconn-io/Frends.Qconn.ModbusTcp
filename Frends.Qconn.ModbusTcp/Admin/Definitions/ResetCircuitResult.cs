namespace Frends.Qconn.ModbusTcp.Admin.Definitions;

/// <summary>Outcome of the ResetCircuit admin Task.</summary>
public class ResetCircuitResult
{
    /// <summary>True if a breaker existed for the device and was reset. False if no breaker was tracked
    /// (no action needed).</summary>
    public bool Reset { get; }

    public ResetCircuitResult(bool reset)
    {
        Reset = reset;
    }
}
