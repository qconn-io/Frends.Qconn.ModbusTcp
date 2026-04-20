namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Modbus framing mode over the TCP transport.</summary>
public enum TransportMode
{
    /// <summary>Native Modbus TCP (default). ADU = MBAP header + PDU.</summary>
    TcpNative,

    /// <summary>RTU frames (with CRC) wrapped in TCP. Used by serial-to-Ethernet gateways.
    /// Reserved for a later milestone; selecting it in v2.0 Milestone 1 throws NotSupportedException at connect time.</summary>
    RtuOverTcp,
}
