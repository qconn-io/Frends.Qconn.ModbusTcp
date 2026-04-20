using System;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Helpers for retry scheduling. The retry LOOP itself lives in the Task methods
/// (Read.ReadData / ReadBatch.ReadBatchData / Write.*) because they construct typed Result objects
/// and need access to the attempt history for Diagnostics population.</summary>
internal static class RetryExecutor
{
    private static Func<TimeSpan, CancellationToken, Task> delay = (ts, ct) => Task.Delay(ts, ct);

    /// <summary>Override for deterministic tests (e.g., instant-return delay).</summary>
    internal static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } =
        (ts, ct) => Task.Delay(ts, ct);

    /// <summary>Whether this error category should trigger a retry given the configured RetryOn mask.
    /// Modbus exception codes 1/2/3 never retry regardless.</summary>
    public static bool ShouldRetry(ErrorCategory category, int? modbusCode, RetryOptions opts)
    {
        if (opts.MaxAttempts <= 1) return false;
        if (opts.RetryOn == RetryableCategories.None) return false;

        return category switch
        {
            ErrorCategory.Timeout => (opts.RetryOn & RetryableCategories.Timeout) != 0,
            ErrorCategory.ConnectionRefused => (opts.RetryOn & RetryableCategories.SocketError) != 0,
            ErrorCategory.HostUnreachable => (opts.RetryOn & RetryableCategories.SocketError) != 0,
            ErrorCategory.SocketError => (opts.RetryOn & RetryableCategories.SocketError) != 0,
            ErrorCategory.SlaveBusy => (opts.RetryOn & RetryableCategories.SlaveBusy) != 0,
            ErrorCategory.GatewayTimeout => (opts.RetryOn & RetryableCategories.GatewayTimeout) != 0,
            ErrorCategory.ModbusException => modbusCode switch
            {
                6 => (opts.RetryOn & RetryableCategories.SlaveBusy) != 0,
                10 or 11 => (opts.RetryOn & RetryableCategories.GatewayTimeout) != 0,
                _ => false, // codes 1/2/3 and others — never retry
            },
            _ => false,
        };
    }

    /// <summary>Returns the delay to wait before attempt N+1 after the Nth attempt failed.
    /// The first retry uses InitialBackoffMs; subsequent retries scale according to strategy, capped at MaxBackoffMs.</summary>
    public static TimeSpan ComputeBackoff(int failedAttempt, RetryOptions opts, Random? rng = null)
    {
        rng ??= ThreadLocalRandom.Value!;
        long baseMs = opts.InitialBackoffMs;
        long delayMs = opts.BackoffStrategy switch
        {
            BackoffStrategy.Fixed => baseMs,
            BackoffStrategy.Exponential => Math.Min(baseMs * (1L << Math.Min(failedAttempt - 1, 30)), opts.MaxBackoffMs),
            BackoffStrategy.ExponentialWithJitter =>
                Math.Min((long)(baseMs * (1L << Math.Min(failedAttempt - 1, 30)) * (0.5 + rng.NextDouble())), opts.MaxBackoffMs),
            _ => baseMs,
        };
        if (delayMs < 0) delayMs = opts.MaxBackoffMs;
        return TimeSpan.FromMilliseconds(delayMs);
    }

    private static readonly ThreadLocal<Random> ThreadLocalRandom =
        new(() => new Random(Guid.NewGuid().GetHashCode()));

    public static Task DelayAsync(TimeSpan duration, CancellationToken ct) => Delay(duration, ct);
}
