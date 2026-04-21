using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Options for Modbus TCP write operations.
/// ThrowOnFailure defaults to true (silent write failures to control systems are hazardous).
/// Retry is disabled by default — retrying a non-idempotent write re-applies its effect.</summary>
public class WriteOptions : IModbusOptions
{
    /// <summary>Byte and word order for multi-register values (Int32, UInt32, Float32, Float64).
    /// Default is BigEndian (high word first) — Modbus spec default.
    /// Check your device manual; mismatch here is the #1 cause of wrong values written.</summary>
    [DefaultValue(ByteOrder.BigEndian)]
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;

    /// <summary>Addressing convention. ZeroBased = address sent on the wire as-is.
    /// ModiconOneBased = subtract 1 before sending. Use when your device manual shows addresses like "40001".</summary>
    [DefaultValue(AddressingMode.ZeroBased)]
    public AddressingMode AddressingMode { get; set; } = AddressingMode.ZeroBased;

    /// <summary>Linear scale applied when encoding numeric values: wire_value = (value - Offset) / Scale.
    /// Default 1.0 (no scaling).</summary>
    [DefaultValue(1.0)]
    public double Scale { get; set; } = 1.0;

    /// <summary>Offset applied before scale inversion: wire_value = (value - Offset) / Scale. Default 0.</summary>
    [DefaultValue("0")]
    [DisplayFormat(DataFormatString = "Expression")]
    public double Offset { get; set; } = 0.0;

    /// <summary>TCP socket connect timeout in milliseconds.</summary>
    [DefaultValue(5000)]
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>Per-operation timeout in milliseconds.</summary>
    [DefaultValue(5000)]
    public int ReadTimeoutMs { get; set; } = 5000;

    /// <summary>If true (default for writes), the Task throws on failure instead of returning Success=false.
    /// Silent write failures to control systems are hazardous; set to false only if the caller
    /// explicitly handles partial success.</summary>
    [DefaultValue(true)]
    public bool ThrowOnFailure { get; set; } = true;

    /// <summary>Modbus framing over TCP. Default TcpNative. RtuOverTcp is reserved for a later milestone
    /// and currently throws NotSupportedException at connect time.</summary>
    [DefaultValue(TransportMode.TcpNative)]
    public TransportMode TransportMode { get; set; } = TransportMode.TcpNative;

    /// <summary>Optional device-profile identifier. Reserved for a later milestone — ignored today.</summary>
    [DefaultValue(null)]
    public string? DeviceProfile { get; set; } = null;

    /// <summary>Connection-pool settings.</summary>
    public PoolOptions Pool { get; set; } = new PoolOptions();

    /// <summary>Retry settings. Default MaxAttempts = 1 (no retry). Set MaxAttempts &gt; 1 only if the
    /// target register is idempotent — otherwise a retried write may apply its effect twice.</summary>
    public RetryOptions Retry { get; set; } = new RetryOptions { MaxAttempts = 1 };

    /// <summary>Circuit-breaker settings.</summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new CircuitBreakerOptions();

    /// <summary>Set to false to block this Task from executing any write. Use a Frends Environment Variable
    /// to control access per Environment without changing Process logic: in the Frends Management portal
    /// go to Environments - Variables, choose a group (e.g. Modbus) and add variable AllowWrites with
    /// value true or false, then set this field to #env.Modbus.AllowWrites in the Process editor.
    /// Default is true.</summary>
    [DefaultValue(true)]
    public bool AllowWrites { get; set; } = true;
}
