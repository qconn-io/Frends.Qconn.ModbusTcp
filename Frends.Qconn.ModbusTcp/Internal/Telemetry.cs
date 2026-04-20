using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>OpenTelemetry primitives (Meter + ActivitySource) for Modbus TCP Task operations.
/// Emission is unconditional; the Agent process decides whether to collect.</summary>
internal static class Telemetry
{
    public const string Name = "Frends.Qconn.ModbusTcp";
    public const string Version = "2.0";

    public static readonly ActivitySource ActivitySource = new(Name, Version);
    public static readonly Meter Meter = new(Name, Version);

    public static readonly Counter<long> Operations =
        Meter.CreateCounter<long>("modbus.operations", description: "Modbus operations executed.");

    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("modbus.duration", unit: "ms", description: "Modbus operation duration.");

    public static readonly Counter<long> Errors =
        Meter.CreateCounter<long>("modbus.errors", description: "Modbus operation errors.");

    public static readonly Counter<long> RetryCount =
        Meter.CreateCounter<long>("modbus.retry.count", description: "Retries attempted.");

    public static readonly Counter<long> BackpressureRejected =
        Meter.CreateCounter<long>("modbus.backpressure.rejected", description: "Operations rejected due to per-device queue depth.");

    public static readonly Counter<long> ConnectionsEvicted =
        Meter.CreateCounter<long>("modbus.connection.evicted", description: "Pooled connections evicted due to idle timeout.");
}
