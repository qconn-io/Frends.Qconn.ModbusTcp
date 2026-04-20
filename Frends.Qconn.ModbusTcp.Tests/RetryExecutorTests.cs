using System;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Unit tests for retry classification and backoff math.</summary>
public class RetryExecutorTests
{
    [Fact]
    public void MaxAttempts_One_Never_Retries()
    {
        var opts = new RetryOptions { MaxAttempts = 1, RetryOn = RetryableCategories.All };
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.Timeout, null, opts));
    }

    [Fact]
    public void Timeout_Matches_Timeout_Mask()
    {
        var opts = new RetryOptions { MaxAttempts = 3, RetryOn = RetryableCategories.Timeout };
        Assert.True(RetryExecutor.ShouldRetry(ErrorCategory.Timeout, null, opts));
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.ConnectionRefused, null, opts));
    }

    [Fact]
    public void SocketError_Mask_Covers_Refused_And_Unreachable()
    {
        var opts = new RetryOptions { MaxAttempts = 3, RetryOn = RetryableCategories.SocketError };
        Assert.True(RetryExecutor.ShouldRetry(ErrorCategory.ConnectionRefused, null, opts));
        Assert.True(RetryExecutor.ShouldRetry(ErrorCategory.HostUnreachable, null, opts));
        Assert.True(RetryExecutor.ShouldRetry(ErrorCategory.SocketError, null, opts));
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.Timeout, null, opts));
    }

    [Fact]
    public void Modbus_Codes_123_Never_Retry()
    {
        var opts = new RetryOptions { MaxAttempts = 3, RetryOn = RetryableCategories.All };
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.ModbusException, 1, opts));
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.ModbusException, 2, opts));
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.ModbusException, 3, opts));
    }

    [Fact]
    public void Modbus_Code_6_Retries_With_SlaveBusy_Mask()
    {
        var opts = new RetryOptions { MaxAttempts = 3, RetryOn = RetryableCategories.SlaveBusy };
        Assert.True(RetryExecutor.ShouldRetry(ErrorCategory.ModbusException, 6, opts));
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.ModbusException, 10, opts));
    }

    [Fact]
    public void Modbus_Codes_10_11_Retry_With_GatewayTimeout_Mask()
    {
        var opts = new RetryOptions { MaxAttempts = 3, RetryOn = RetryableCategories.GatewayTimeout };
        Assert.True(RetryExecutor.ShouldRetry(ErrorCategory.ModbusException, 10, opts));
        Assert.True(RetryExecutor.ShouldRetry(ErrorCategory.ModbusException, 11, opts));
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.ModbusException, 6, opts));
    }

    [Fact]
    public void DecodingError_And_InvalidInput_Never_Retry()
    {
        var opts = new RetryOptions { MaxAttempts = 3, RetryOn = RetryableCategories.All };
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.DecodingError, null, opts));
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.InvalidInput, null, opts));
        Assert.False(RetryExecutor.ShouldRetry(ErrorCategory.Cancelled, null, opts));
    }

    [Fact]
    public void Fixed_Backoff_Returns_Initial()
    {
        var opts = new RetryOptions { InitialBackoffMs = 200, BackoffStrategy = BackoffStrategy.Fixed, MaxBackoffMs = 5000 };
        Assert.Equal(TimeSpan.FromMilliseconds(200), RetryExecutor.ComputeBackoff(1, opts));
        Assert.Equal(TimeSpan.FromMilliseconds(200), RetryExecutor.ComputeBackoff(5, opts));
    }

    [Fact]
    public void Exponential_Backoff_Doubles()
    {
        var opts = new RetryOptions { InitialBackoffMs = 100, BackoffStrategy = BackoffStrategy.Exponential, MaxBackoffMs = 5000 };
        Assert.Equal(TimeSpan.FromMilliseconds(100), RetryExecutor.ComputeBackoff(1, opts));
        Assert.Equal(TimeSpan.FromMilliseconds(200), RetryExecutor.ComputeBackoff(2, opts));
        Assert.Equal(TimeSpan.FromMilliseconds(400), RetryExecutor.ComputeBackoff(3, opts));
    }

    [Fact]
    public void Exponential_Backoff_Respects_Cap()
    {
        var opts = new RetryOptions { InitialBackoffMs = 100, BackoffStrategy = BackoffStrategy.Exponential, MaxBackoffMs = 500 };
        Assert.Equal(TimeSpan.FromMilliseconds(100), RetryExecutor.ComputeBackoff(1, opts));
        Assert.Equal(TimeSpan.FromMilliseconds(200), RetryExecutor.ComputeBackoff(2, opts));
        Assert.Equal(TimeSpan.FromMilliseconds(400), RetryExecutor.ComputeBackoff(3, opts));
        Assert.Equal(TimeSpan.FromMilliseconds(500), RetryExecutor.ComputeBackoff(4, opts));
    }

    [Fact]
    public void Jitter_Backoff_In_Expected_Range()
    {
        var opts = new RetryOptions { InitialBackoffMs = 1000, BackoffStrategy = BackoffStrategy.ExponentialWithJitter, MaxBackoffMs = 10_000 };
        var rng = new Random(42);
        var delay = RetryExecutor.ComputeBackoff(2, opts, rng);
        // Expected base at attempt 2 is 2 * 1000 = 2000ms. Jitter factor [0.5, 1.5) → [1000, 3000).
        Assert.InRange(delay.TotalMilliseconds, 1000, 3000);
    }
}
