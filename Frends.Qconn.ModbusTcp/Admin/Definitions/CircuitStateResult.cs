using System;

namespace Frends.Qconn.ModbusTcp.Admin.Definitions;

/// <summary>Snapshot of a device's circuit-breaker state.</summary>
public class CircuitStateResult
{
    /// <summary>True if a breaker exists for the requested device. False means the device has never been used
    /// by this Agent since process start (and so has no tracked state).</summary>
    public bool Exists { get; }

    /// <summary>"Closed", "Open", or "HalfOpen". Null when Exists is false.</summary>
    public string? State { get; }

    /// <summary>Consecutive failures counted toward the open threshold.</summary>
    public int FailureCount { get; }

    /// <summary>UTC timestamp of the most recent failure that counted toward the breaker. Null if none recorded.</summary>
    public DateTimeOffset? LastFailureUtc { get; }

    /// <summary>UTC timestamp at which the Open window expires and the next request will transition to HalfOpen.
    /// Null when State is not Open.</summary>
    public DateTimeOffset? OpenUntilUtc { get; }

    public CircuitStateResult(bool exists, string? state, int failureCount,
        DateTimeOffset? lastFailureUtc, DateTimeOffset? openUntilUtc)
    {
        Exists = exists;
        State = state;
        FailureCount = failureCount;
        LastFailureUtc = lastFailureUtc;
        OpenUntilUtc = openUntilUtc;
    }
}
