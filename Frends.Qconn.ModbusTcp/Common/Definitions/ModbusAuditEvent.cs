using System;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Common.Definitions;

/// <summary>Structured audit event emitted on every Modbus operation (read/write/admin).
/// Audit events are never redacted by Frends-Agent-level DoNotLog flags; security-critical fields stay visible.</summary>
public sealed class ModbusAuditEvent
{
    public DateTimeOffset Timestamp { get; init; }
    public string? AgentName { get; init; }
    public string? EnvironmentName { get; init; }
    public string? ProcessName { get; init; }
    public Guid? ProcessInstanceId { get; init; }
    public string? InitiatedBy { get; init; }

    /// <summary>"Read", "ReadBatch", "WriteSingleCoil", "WriteSingleRegister", "WriteMultiple", "ReadWriteMultiple", "WriteBatch", "ResetCircuit", etc.</summary>
    public string Operation { get; init; } = string.Empty;

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public byte UnitId { get; init; }

    /// <summary>Modbus function code (1/2/3/4/5/6/15/16/23). Zero for admin events.</summary>
    public int FunctionCode { get; init; }

    public ushort StartAddress { get; init; }
    public ushort Count { get; init; }

    public TransportSecurity TransportSecurity { get; init; }
    public string? ClientCertificateSubject { get; init; }
    public string? ModbusRole { get; init; }

    public bool Success { get; init; }
    public string? ErrorCategory { get; init; }
    public int? ModbusExceptionCode { get; init; }
    public int AttemptCount { get; init; } = 1;
    public long TotalTimeMs { get; init; }

    /// <summary>For writes: the full values payload sent. Null for reads and admin ops.</summary>
    public object? ValuesWritten { get; init; }
}
