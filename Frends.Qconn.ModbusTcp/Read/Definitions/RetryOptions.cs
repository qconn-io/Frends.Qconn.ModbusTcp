using System.ComponentModel;

namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Retry behavior for Modbus operations. Default is MaxAttempts = 1 (single-shot, matches v1 semantics).
/// Set MaxAttempts &gt; 1 to enable retry of transient failures.</summary>
public class RetryOptions
{
    /// <summary>Maximum attempts (including the first). Default 1 — no retry, preserves v1 behavior.
    /// Set to 3 or 4 for lossy networks. Write Tasks override this default to 1 regardless.</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public int MaxAttempts { get; set; } = 1;

    /// <summary>First-retry delay in milliseconds. Exponential / jitter strategies use this as the base.</summary>
    /// <example>200</example>
    [DefaultValue(200)]
    public int InitialBackoffMs { get; set; } = 200;

    /// <summary>How the delay grows between attempts.</summary>
    /// <example>ExponentialWithJitter</example>
    [DefaultValue(BackoffStrategy.ExponentialWithJitter)]
    public BackoffStrategy BackoffStrategy { get; set; } = BackoffStrategy.ExponentialWithJitter;

    /// <summary>Upper bound on any single retry delay, in milliseconds. Caps Exponential growth.</summary>
    /// <example>10000</example>
    [DefaultValue(10000)]
    public int MaxBackoffMs { get; set; } = 10000;

    /// <summary>Which failure categories to retry. Defaults to all four transient categories.
    /// Modbus exception codes 1/2/3 never retry regardless.</summary>
    /// <example>All</example>
    [DefaultValue(RetryableCategories.All)]
    public RetryableCategories RetryOn { get; set; } = RetryableCategories.All;
}
