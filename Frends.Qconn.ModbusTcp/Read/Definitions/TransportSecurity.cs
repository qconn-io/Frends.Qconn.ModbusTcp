namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Transport-level security for the Modbus TCP connection.
/// Only None is wired in v2.0 Milestone 1; Tls and MutualTls are reserved
/// for a later milestone and selecting them throws NotSupportedException at connect time.</summary>
public enum TransportSecurity
{
    /// <summary>Plaintext Modbus TCP (spec default on port 502). No encryption, no authentication.</summary>
    None,

    /// <summary>Modbus TCP Security (Modbus Organization 2018 spec) with TLS 1.2+ on port 802.
    /// Reserved for a later milestone.</summary>
    Tls,

    /// <summary>TLS with mandatory mutual authentication. Reserved for a later milestone.</summary>
    MutualTls,
}
