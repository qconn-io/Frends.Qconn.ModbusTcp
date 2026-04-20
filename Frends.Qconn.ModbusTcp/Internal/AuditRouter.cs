using System;
using System.Diagnostics;
using Frends.Qconn.ModbusTcp.Common.Definitions;
using Frends.Qconn.ModbusTcp.Internal.AuditSinks;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Routes audit events to the configured sink (env var ModbusTcp.AuditSink).
/// Valid values: "FrendsLog" (default), "File", "Syslog", "OpenTelemetry", "None".</summary>
internal static class AuditRouter
{
    private static readonly Lazy<IAuditSink> Sink = new(CreateSink);
    private static readonly Lazy<IAgentContextAccessor> Context = new(() => new ReflectionAgentContextAccessor());

    /// <summary>Allows tests to intercept audit emissions.</summary>
    internal static IAuditSink? TestOverride { get; set; }

    public static void Emit(ModbusAuditEvent evt)
    {
        try
        {
            (TestOverride ?? Sink.Value).Emit(evt);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[modbus.audit.error] sink.Emit threw: {ex.Message}");
        }
    }

    public static IAgentContextAccessor AgentContext => Context.Value;

    private static IAuditSink CreateSink()
    {
        var choice = Environment.GetEnvironmentVariable("ModbusTcp.AuditSink")?.Trim();
        return (choice ?? "FrendsLog").ToLowerInvariant() switch
        {
            "none" => LogStartupWarning(new NoopAuditSink()),
            "file" => new FileAuditSink(
                Environment.GetEnvironmentVariable("ModbusTcp.AuditFilePath")
                    ?? "./modbus-audit.jsonl"),
            "syslog" => new SyslogAuditSink(
                Environment.GetEnvironmentVariable("ModbusTcp.SyslogHost") ?? "127.0.0.1",
                int.TryParse(Environment.GetEnvironmentVariable("ModbusTcp.SyslogPort"), out var p) ? p : 514),
            "opentelemetry" or "otel" => new OpenTelemetryAuditSink(),
            _ => new FrendsLogAuditSink(),
        };
    }

    private static IAuditSink LogStartupWarning(IAuditSink sink)
    {
        Trace.WriteLine("[modbus.audit.warn] ModbusTcp.AuditSink=None: Modbus operations will not be audited. This is not recommended for Production Environments.");
        return sink;
    }
}
