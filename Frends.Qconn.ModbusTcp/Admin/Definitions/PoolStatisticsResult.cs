using System;

namespace Frends.Qconn.ModbusTcp.Admin.Definitions;

/// <summary>Snapshot of Agent-wide Modbus connection pool state, returned by the PoolStatistics admin Task.</summary>
public class PoolStatisticsResult
{
    /// <summary>Total pooled connections across all devices.</summary>
    public int TotalConnections { get; }

    /// <summary>Connections currently serving an operation.</summary>
    public int ActiveConnections { get; }

    /// <summary>Connections sitting idle in the pool.</summary>
    public int IdleConnections { get; }

    /// <summary>Per-device breakdown.</summary>
    public PerDeviceStatistics[] PerDevice { get; }

    public PoolStatisticsResult(int totalConnections, int activeConnections, int idleConnections, PerDeviceStatistics[] perDevice)
    {
        TotalConnections = totalConnections;
        ActiveConnections = activeConnections;
        IdleConnections = idleConnections;
        PerDevice = perDevice;
    }
}

/// <summary>Per-device statistics for one entry in PoolStatisticsResult.PerDevice.</summary>
public class PerDeviceStatistics
{
    /// <summary>Target host.</summary>
    public string Host { get; }

    /// <summary>TCP port.</summary>
    public int Port { get; }

    /// <summary>Modbus unit ID.</summary>
    public byte UnitId { get; }

    /// <summary>Number of pooled connections to this device (typically 1 per the MaxConnectionsPerDevice cap).</summary>
    public int Connections { get; }

    /// <summary>Total operations executed against this device since Agent start.</summary>
    public long TotalOperations { get; }

    /// <summary>Total errors recorded for this device since Agent start.</summary>
    public long TotalErrors { get; }

    /// <summary>UTC timestamp of the most recent use of any pooled connection to this device.</summary>
    public DateTimeOffset LastUsedUtc { get; }

    public PerDeviceStatistics(string host, int port, byte unitId, int connections,
        long totalOperations, long totalErrors, DateTimeOffset lastUsedUtc)
    {
        Host = host;
        Port = port;
        UnitId = unitId;
        Connections = connections;
        TotalOperations = totalOperations;
        TotalErrors = totalErrors;
        LastUsedUtc = lastUsedUtc;
    }
}
