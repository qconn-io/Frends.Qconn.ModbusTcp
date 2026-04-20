using System;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Unit tests for the per-device circuit breaker state machine.</summary>
public class CircuitBreakerTests
{
    [Fact]
    public void Closed_Allows_Operations()
    {
        var cb = new CircuitBreaker(new CircuitBreakerOptions { Enabled = true, FailureThreshold = 3 });
        Assert.True(cb.CanPass(() => DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Consecutive_Failures_Open_Breaker()
    {
        var now = DateTimeOffset.UtcNow;
        var cb = new CircuitBreaker(new CircuitBreakerOptions { Enabled = true, FailureThreshold = 3, OpenDurationMs = 10_000 });

        cb.RecordFailure(() => now);
        cb.RecordFailure(() => now);
        Assert.True(cb.CanPass(() => now)); // 2 failures — still below threshold
        cb.RecordFailure(() => now);
        Assert.False(cb.CanPass(() => now)); // 3 failures — now open
    }

    [Fact]
    public void Open_Transitions_To_HalfOpen_After_Duration()
    {
        var now = DateTimeOffset.UtcNow;
        var cb = new CircuitBreaker(new CircuitBreakerOptions { Enabled = true, FailureThreshold = 1, OpenDurationMs = 1000 });

        cb.RecordFailure(() => now);
        Assert.False(cb.CanPass(() => now));

        // Advance clock past OpenDurationMs
        var later = now.AddMilliseconds(1500);
        Assert.True(cb.CanPass(() => later)); // Transitioned to HalfOpen
    }

    [Fact]
    public void HalfOpen_Success_Closes_Breaker()
    {
        var now = DateTimeOffset.UtcNow;
        var cb = new CircuitBreaker(new CircuitBreakerOptions { Enabled = true, FailureThreshold = 1, OpenDurationMs = 100, SuccessThresholdToClose = 1 });

        cb.RecordFailure(() => now);
        cb.CanPass(() => now.AddMilliseconds(500)); // transitions to HalfOpen
        cb.RecordSuccess();

        // Breaker should now be Closed — new failures start fresh counter
        Assert.True(cb.CanPass(() => now.AddMilliseconds(600)));
        Assert.Equal(CircuitBreaker.State.Closed, cb.Snapshot().State);
    }

    [Fact]
    public void HalfOpen_Failure_Reopens()
    {
        var now = DateTimeOffset.UtcNow;
        var cb = new CircuitBreaker(new CircuitBreakerOptions { Enabled = true, FailureThreshold = 1, OpenDurationMs = 100 });

        cb.RecordFailure(() => now);
        cb.CanPass(() => now.AddMilliseconds(500)); // → HalfOpen
        cb.RecordFailure(() => now.AddMilliseconds(500));

        Assert.Equal(CircuitBreaker.State.Open, cb.Snapshot().State);
    }

    [Fact]
    public void Modbus_Client_Error_Codes_Do_Not_Count()
    {
        // Codes 1, 2, 3 are client-side bugs — not device health issues.
        Assert.False(CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, 1));
        Assert.False(CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, 2));
        Assert.False(CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, 3));
    }

    [Fact]
    public void Modbus_Device_Error_Codes_Count()
    {
        Assert.True(CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, 4));
        Assert.True(CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, 5));
        Assert.True(CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, 6));
        Assert.True(CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, 10));
        Assert.True(CircuitBreaker.CountsAsFailure(ErrorCategory.ModbusException, 11));
    }

    [Fact]
    public void Decoding_Error_Does_Not_Count()
    {
        // Decoding errors indicate a schema mismatch, not device health.
        Assert.False(CircuitBreaker.CountsAsFailure(ErrorCategory.DecodingError, null));
    }

    [Fact]
    public void Timeout_And_Socket_Errors_Count()
    {
        Assert.True(CircuitBreaker.CountsAsFailure(ErrorCategory.Timeout, null));
        Assert.True(CircuitBreaker.CountsAsFailure(ErrorCategory.ConnectionRefused, null));
        Assert.True(CircuitBreaker.CountsAsFailure(ErrorCategory.HostUnreachable, null));
    }

    [Fact]
    public void Disabled_Breaker_Always_Allows()
    {
        var cb = new CircuitBreaker(new CircuitBreakerOptions { Enabled = false, FailureThreshold = 1 });
        cb.RecordFailure(() => DateTimeOffset.UtcNow);
        cb.RecordFailure(() => DateTimeOffset.UtcNow);
        Assert.True(cb.CanPass(() => DateTimeOffset.UtcNow));
    }
}
