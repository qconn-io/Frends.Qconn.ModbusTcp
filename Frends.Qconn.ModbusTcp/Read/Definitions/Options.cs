using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Internal;

namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Behavioral options for the Modbus TCP read operation.</summary>
public class Options : IModbusOptions
{
    /// <summary>Byte and word order for multi-register values (Int32, UInt32, Float32, Float64).
    /// Ignored for Raw, Int16, UInt16, AsciiString, and coil reads.
    /// Default is BigEndian (high word first, high byte first) — Modbus spec default.
    /// Use LittleEndianWordSwap for Modicon/Schneider-style "low word first".
    /// Check your device manual; mismatch here is the #1 cause of wrong readings.</summary>
    /// <example>BigEndian</example>
    [DefaultValue(ByteOrder.BigEndian)]
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;

    /// <summary>Addressing convention. ZeroBased = the address is sent on the wire as-is (matches Modbus PDU spec).
    /// ModiconOneBased = subtract 1 from the address before sending, and strip leading 4xxxx/3xxxx/1xxxx prefix
    /// if present. Use ModiconOneBased when your device manual shows addresses like "40001".</summary>
    /// <example>ZeroBased</example>
    [DefaultValue(AddressingMode.ZeroBased)]
    public AddressingMode AddressingMode { get; set; } = AddressingMode.ZeroBased;

    /// <summary>Linear scale factor applied to decoded numeric values: output = (raw * Scale) + Offset.
    /// Example: meter returns 2304 for 230.4 V → use Scale = 0.1.
    /// Applied only to numeric ValueTypes; never to Raw, AsciiString, or coil reads.</summary>
    /// <example>1.0</example>
    [DefaultValue(1.0)]
    public double Scale { get; set; } = 1.0;

    /// <summary>Offset added after Scale: output = (raw * Scale) + Offset. See Scale.</summary>
    /// <example>0.0</example>
    [DefaultValue(0.0)]
    public double Offset { get; set; } = 0.0;

    /// <summary>TCP socket connect timeout in milliseconds.</summary>
    /// <example>5000</example>
    [DefaultValue(5000)]
    public int ConnectTimeoutMs { get; set; } = 5000;

    /// <summary>Per-read timeout in milliseconds. Does not cover connect time; see ConnectTimeoutMs.</summary>
    /// <example>5000</example>
    [DefaultValue(5000)]
    public int ReadTimeoutMs { get; set; } = 5000;

    /// <summary>If true, the Task throws an exception instead of returning a Result with Success=false.
    /// Default false. Frends convention: prefer structured Results so Processes can route with
    /// Exclusive Decision shapes rather than error handlers.</summary>
    /// <example>false</example>
    [DefaultValue(false)]
    public bool ThrowOnFailure { get; set; } = false;

    // ----------------------- v2 additions (appended; fields 1–7 above are v1-frozen) -----------------------

    /// <summary>Modbus framing over TCP. Default TcpNative. RtuOverTcp is reserved for a later milestone
    /// and currently throws NotSupportedException at connect time.</summary>
    /// <example>TcpNative</example>
    [DefaultValue(TransportMode.TcpNative)]
    public TransportMode TransportMode { get; set; } = TransportMode.TcpNative;

    /// <summary>Optional device-profile identifier. When set, defaults from the named profile pre-fill any Options
    /// and Input fields the caller has left at their default value. Reserved for a later milestone — ignored today.</summary>
    /// <example>null</example>
    [DefaultValue(null)]
    public string? DeviceProfile { get; set; } = null;

    /// <summary>Connection-pool settings. Defaults reuse sockets transparently.</summary>
    public PoolOptions Pool { get; set; } = new PoolOptions();

    /// <summary>Retry settings. Default MaxAttempts = 1 preserves v1 single-shot behavior;
    /// set MaxAttempts &gt; 1 to enable retry of transient failures.</summary>
    public RetryOptions Retry { get; set; } = new RetryOptions();

    /// <summary>Circuit-breaker settings. Default Enabled = true with a 5-failure threshold
    /// and 30-second Open duration.</summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new CircuitBreakerOptions();
}
