using System;

namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Which failure categories trigger a retry when Options.Retry.MaxAttempts > 1.
/// Combine with bitwise OR. Modbus exception codes 1/2/3 (client-side bugs) never retry regardless.</summary>
[Flags]
public enum RetryableCategories
{
    /// <summary>Never retry; use this with MaxAttempts = 1 for strict single-shot semantics.</summary>
    None = 0,

    /// <summary>Retry on connect/read timeouts.</summary>
    Timeout = 1,

    /// <summary>Retry on TCP socket errors (refused, unreachable, network error).</summary>
    SocketError = 2,

    /// <summary>Retry on Modbus exception code 6 (SlaveDeviceBusy).</summary>
    SlaveBusy = 4,

    /// <summary>Retry on Modbus exception codes 10/11 (gateway path unavailable / target no response).</summary>
    GatewayTimeout = 8,

    /// <summary>All transient categories.</summary>
    All = Timeout | SocketError | SlaveBusy | GatewayTimeout,
}
