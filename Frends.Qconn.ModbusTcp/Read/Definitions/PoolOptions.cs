using System.ComponentModel;

namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>TCP connection pool settings. Pooled connections are reused across reads and writes
/// targeting the same (Host, Port, UnitId, transport) tuple to avoid TCP setup cost on every poll.</summary>
public class PoolOptions
{
    /// <summary>Use the Agent-wide connection pool. Default true — the pool is transparent to callers
    /// and reuses sockets to the same device across Tasks. Disable to force a fresh connection per call
    /// (matches v1.0 behavior exactly).</summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool UseConnectionPool { get; set; } = true;

    /// <summary>Idle time in milliseconds before a pooled connection is evicted and the socket closed.
    /// Default 60000. Lower this for devices that close idle connections aggressively.</summary>
    /// <example>60000</example>
    [DefaultValue(60000)]
    public int IdleTimeoutMs { get; set; } = 60000;

    /// <summary>Maximum simultaneous TCP connections to the same device (Host + Port + UnitId).
    /// Default 1. Increase only if the target device explicitly supports concurrent Modbus connections.
    /// When 0, falls back to the <c>ModbusTcp.MaxConnectionsPerDevice</c> Frends Environment Variable (default 1).</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public int MaxConnectionsPerDevice { get; set; } = 1;

    /// <summary>Max time in milliseconds to wait for a per-device connection slot when it is busy.
    /// Default 5000. Exceeding this surfaces ErrorCategory.Backpressure.</summary>
    /// <example>5000</example>
    [DefaultValue(5000)]
    public int AcquireTimeoutMs { get; set; } = 5000;

    /// <summary>Mirror circuit-breaker state to Frends Shared State so other Agents in the group see the same state.
    /// Default false. Note: in v2.0 Milestone 1 this flag is reserved — setting it to true logs one WARN per Agent
    /// lifetime and falls back to local-only breaker state.</summary>
    /// <example>false</example>
    [DefaultValue(false)]
    public bool ShareCircuitStateAcrossAgents { get; set; } = false;
}
