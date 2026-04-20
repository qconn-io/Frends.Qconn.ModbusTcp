using System.Diagnostics;
using Frends.Qconn.ModbusTcp.Common.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal.AuditSinks;

/// <summary>OpenTelemetry log-record emitter. Writes structured tags via ActivitySource so any OTEL-enabled
/// Agent collects them. No hard dependency on the OpenTelemetry SDK — the Meter + ActivitySource under
/// Telemetry.cs publish to any subscriber configured by the Agent host.</summary>
internal sealed class OpenTelemetryAuditSink : IAuditSink
{
    public void Emit(ModbusAuditEvent evt)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("modbus.audit", ActivityKind.Internal);
        if (activity is null) return;

        activity.SetTag("timestamp", evt.Timestamp);
        activity.SetTag("agent.name", evt.AgentName);
        activity.SetTag("environment.name", evt.EnvironmentName);
        activity.SetTag("process.name", evt.ProcessName);
        activity.SetTag("process.instance_id", evt.ProcessInstanceId?.ToString());
        activity.SetTag("initiated_by", evt.InitiatedBy);
        activity.SetTag("operation", evt.Operation);
        activity.SetTag("network.peer.address", evt.Host);
        activity.SetTag("network.peer.port", evt.Port);
        activity.SetTag("modbus.unit_id", evt.UnitId);
        activity.SetTag("modbus.function_code", evt.FunctionCode);
        activity.SetTag("modbus.address", evt.StartAddress);
        activity.SetTag("modbus.count", evt.Count);
        activity.SetTag("modbus.role", evt.ModbusRole);
        activity.SetTag("tls.mode", evt.TransportSecurity.ToString());
        activity.SetTag("tls.client_certificate_subject", evt.ClientCertificateSubject);
        activity.SetTag("success", evt.Success);
        activity.SetTag("error.category", evt.ErrorCategory);
        activity.SetTag("modbus.exception_code", evt.ModbusExceptionCode);
        activity.SetTag("attempt.count", evt.AttemptCount);
        activity.SetTag("duration.ms", evt.TotalTimeMs);

        if (!evt.Success)
            activity.SetStatus(ActivityStatusCode.Error, evt.ErrorCategory);
    }
}
