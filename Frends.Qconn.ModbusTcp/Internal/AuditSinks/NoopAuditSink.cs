using Frends.Qconn.ModbusTcp.Common.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal.AuditSinks;

/// <summary>Audit sink that discards all events. Used when ModbusTcp.AuditSink=None.</summary>
internal sealed class NoopAuditSink : IAuditSink
{
    public void Emit(ModbusAuditEvent evt)
    {
    }
}
