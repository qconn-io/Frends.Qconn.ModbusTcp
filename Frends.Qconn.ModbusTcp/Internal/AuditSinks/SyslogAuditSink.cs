using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Newtonsoft.Json;

namespace Frends.Qconn.ModbusTcp.Internal.AuditSinks;

/// <summary>RFC 5424 syslog emitter over UDP. Enable with ModbusTcp.AuditSink=Syslog
/// and ModbusTcp.SyslogHost=host, ModbusTcp.SyslogPort=514.</summary>
internal sealed class SyslogAuditSink : IAuditSink
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        NullValueHandling = NullValueHandling.Include,
    };

    private readonly string host;
    private readonly int port;
    private readonly UdpClient udp = new();

    public SyslogAuditSink(string host, int port)
    {
        this.host = host;
        this.port = port;
    }

    public void Emit(ModbusAuditEvent evt)
    {
        try
        {
            // RFC 5424: <PRI>VERSION TIMESTAMP HOSTNAME APP-NAME PROCID MSGID STRUCTURED-DATA MSG
            // PRI = facility*8 + severity. facility=4 (security/authorization), severity=6 (informational) → 38
            var pri = 38;
            var ts = evt.Timestamp.ToString("O");
            var hostName = evt.AgentName ?? "-";
            var msg = JsonConvert.SerializeObject(evt, SerializerSettings);
            var frame = $"<{pri}>1 {ts} {hostName} frends-modbus - modbus.audit - {msg}";
            var bytes = Encoding.UTF8.GetBytes(frame);
            udp.Send(bytes, bytes.Length, host, port);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[modbus.audit.error] syslog sink failed: {ex.Message}");
        }
    }
}
