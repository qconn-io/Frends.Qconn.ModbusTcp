using System;
using System.Diagnostics;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Newtonsoft.Json;

namespace Frends.Qconn.ModbusTcp.Internal.AuditSinks;

/// <summary>Default audit sink. Writes the event as a single-line structured JSON entry to the Process Instance log
/// via Trace. The Frends Agent collects Trace output into Process Instance logs by default.</summary>
internal sealed class FrendsLogAuditSink : IAuditSink
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        NullValueHandling = NullValueHandling.Include,
    };

    public void Emit(ModbusAuditEvent evt)
    {
        try
        {
            var json = JsonConvert.SerializeObject(evt, SerializerSettings);
            Trace.WriteLine("[modbus.audit] " + json);
        }
        catch (Exception ex)
        {
            // Audit must never throw back into the caller — best-effort only.
            Trace.WriteLine($"[modbus.audit.error] failed to serialize audit event: {ex.Message}");
        }
    }
}
