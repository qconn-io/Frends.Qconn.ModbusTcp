using System;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Three-state per-device circuit breaker (Closed / Open / HalfOpen).
/// Thread-safe via a simple lock; state transitions are infrequent.</summary>
internal sealed class CircuitBreaker
{
#pragma warning disable FT0004
    internal enum State
    {
        Closed,
        Open,
        HalfOpen
    }
#pragma warning restore FT0004

    private readonly object gate = new();
    private readonly CircuitBreakerOptions opts;
    private State state = State.Closed;
    private int consecutiveFailures;
    private int halfOpenSuccesses;
    private DateTimeOffset? openedAtUtc;
    private DateTimeOffset? lastFailureUtc;

    public CircuitBreaker(CircuitBreakerOptions opts)
    {
        this.opts = opts;
    }

    /// <summary>Checks whether an operation may proceed. If Open and the open window has elapsed,
    /// transitions to HalfOpen and returns true (allows a probe). Otherwise returns false.</summary>
    public bool CanPass(Func<DateTimeOffset> clock)
    {
        if (!opts.Enabled) return true;
        lock (gate)
        {
            if (state == State.Closed) return true;
            if (state == State.HalfOpen) return true;

            // Open: check whether OpenDurationMs has elapsed
            if (openedAtUtc is { } openedAt &&
                clock() - openedAt >= TimeSpan.FromMilliseconds(opts.OpenDurationMs))
            {
                state = State.HalfOpen;
                halfOpenSuccesses = 0;
                return true;
            }

            return false;
        }
    }

    /// <summary>Called after a successful operation. Closes a HalfOpen breaker once the success threshold is met.</summary>
    public void RecordSuccess()
    {
        if (!opts.Enabled) return;
        lock (gate)
        {
            if (state == State.HalfOpen)
            {
                halfOpenSuccesses++;
                if (halfOpenSuccesses >= opts.SuccessThresholdToClose)
                {
                    state = State.Closed;
                    consecutiveFailures = 0;
                    openedAtUtc = null;
                }
            }
            else
            {
                consecutiveFailures = 0;
            }
        }
    }

    /// <summary>Called after a failure that counts toward breaker opening (see BreakerClassification).
    /// Opens a Closed or HalfOpen breaker once the failure threshold is met.</summary>
    public void RecordFailure(Func<DateTimeOffset> clock)
    {
        if (!opts.Enabled) return;
        lock (gate)
        {
            lastFailureUtc = clock();
            if (state == State.HalfOpen)
            {
                state = State.Open;
                openedAtUtc = clock();
                halfOpenSuccesses = 0;
                return;
            }

            consecutiveFailures++;
            if (consecutiveFailures >= opts.FailureThreshold)
            {
                state = State.Open;
                openedAtUtc = clock();
            }
        }
    }

    /// <summary>Snapshot for CircuitState admin Task.</summary>
    public BreakerSnapshot Snapshot()
    {
        lock (gate)
        {
            return new BreakerSnapshot(
                State: state,
                FailureCount: consecutiveFailures,
                LastFailureUtc: lastFailureUtc,
                OpenUntilUtc: state == State.Open && openedAtUtc.HasValue
                    ? openedAtUtc.Value.AddMilliseconds(opts.OpenDurationMs)
                    : null);
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            state = State.Closed;
            consecutiveFailures = 0;
            halfOpenSuccesses = 0;
            openedAtUtc = null;
            lastFailureUtc = null;
        }
    }

    /// <summary>True for failure categories that should increment the breaker's failure counter.
    /// Explicitly excludes Modbus codes 1/2/3 (client-side bugs — retrying or circuit-breaking makes no sense).</summary>
    public static bool CountsAsFailure(ErrorCategory category, int? modbusExceptionCode)
    {
        return category switch
        {
            ErrorCategory.Timeout => true,
            ErrorCategory.ConnectionRefused => true,
            ErrorCategory.HostUnreachable => true,
            ErrorCategory.SocketError => true,
            ErrorCategory.TlsError => true,
            ErrorCategory.GatewayTimeout => true,
            ErrorCategory.SlaveBusy => true,
            ErrorCategory.ModbusException =>
                // Client-side codes 1/2/3 do not count; device-side codes 4/5/6/10/11 do.
                modbusExceptionCode is 4 or 5 or 6 or 10 or 11,
            _ => false,
        };
    }
}

internal sealed record BreakerSnapshot(
    CircuitBreaker.State State,
    int FailureCount,
    DateTimeOffset? LastFailureUtc,
    DateTimeOffset? OpenUntilUtc);
