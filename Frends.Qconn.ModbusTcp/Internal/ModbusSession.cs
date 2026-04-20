using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Entry point for Modbus operations with pool, retry, and circuit-breaker integration.
/// In v2.0 Milestone 1 this is a thin facade over ConnectionPool; retry and breaker
/// are layered on in Phase 2.</summary>
internal static class ModbusSession
{
    public static Task<ModbusLease> AcquireAsync(
        ConnectionKey key, IModbusOptions options, CancellationToken cancellationToken)
        => ConnectionPool.AcquireAsync(key, options, cancellationToken);
}
