using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Admin.Definitions;
using Frends.Qconn.ModbusTcp.Internal;

namespace Frends.Qconn.ModbusTcp.Admin;

/// <summary>Frends Task for inspecting the Agent-wide Modbus connection pool.</summary>
public static class PoolStatistics
{
    /// <summary>Returns a snapshot of the pool state: total / active / idle connections and per-device counters.
    /// Use this to build Ops dashboards as Frends Processes.</summary>
    /// <param name="cancellationToken">Propagated from the Frends Agent.</param>
    /// <returns>A PoolStatisticsResult captured at the time of the call.</returns>
    public static Task<PoolStatisticsResult> GetStatistics(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = ConnectionPool.Snapshot();
        var perDevice = snapshot.PerDevice
            .Select(d => new PerDeviceStatistics(d.Host, d.Port, d.UnitId, d.Connections,
                d.TotalOperations, d.TotalErrors, d.LastUsedUtc))
            .ToArray();
        return Task.FromResult(new PoolStatisticsResult(
            snapshot.TotalConnections, snapshot.ActiveConnections, snapshot.IdleConnections, perDevice));
    }
}
