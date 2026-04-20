using Frends.Qconn.ModbusTcp.Common.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Destination for ModbusAuditEvent emission. Sinks are plugged in via AuditRouter based on
/// the ModbusTcp.AuditSink environment variable (default FrendsLog).</summary>
internal interface IAuditSink
{
    void Emit(ModbusAuditEvent evt);
}
