using System.ComponentModel;

namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Circuit-breaker behavior per device. Prevents a dead device from eating connection attempts
/// every poll cycle. Modbus exception codes 1/2/3 (client bugs) never count as failures.</summary>
public class CircuitBreakerOptions
{
    /// <summary>Enable the breaker. Default true.</summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    /// <summary>Consecutive failures to open. Default 5.</summary>
    /// <example>5</example>
    [DefaultValue(5)]
    public int FailureThreshold { get; set; } = 5;

    /// <summary>Time to wait in Open state before transitioning to HalfOpen, in milliseconds.</summary>
    /// <example>30000</example>
    [DefaultValue(30000)]
    public int OpenDurationMs { get; set; } = 30000;

    /// <summary>Number of successful HalfOpen probes required to close. Default 1.</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public int SuccessThresholdToClose { get; set; } = 1;
}
