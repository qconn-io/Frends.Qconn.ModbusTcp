namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>How retry backoff grows between attempts.</summary>
public enum BackoffStrategy
{
    /// <summary>Fixed delay equal to InitialBackoffMs between attempts.</summary>
    Fixed,

    /// <summary>Exponential growth: delay_n = InitialBackoffMs * 2^(n-1), capped at MaxBackoffMs.</summary>
    Exponential,

    /// <summary>Exponential growth with uniform-random jitter in [0.5, 1.5) to avoid thundering-herd retries across Agents.</summary>
    ExponentialWithJitter,
}
