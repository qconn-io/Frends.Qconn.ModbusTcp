namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Addressing convention used when interpreting the StartAddress field.</summary>
public enum AddressingMode
{
    /// <summary>Addresses are sent on the wire as-is. Matches Modbus PDU spec and most modern device manuals.</summary>
    ZeroBased,

    /// <summary>Legacy Modicon 1-based addressing: address 40001 maps to holding register 0 on the wire.
    /// Subtracts 1 and strips leading 4xxxx/3xxxx/1xxxx range prefix if present.
    /// Use when your device manual shows addresses like "40001" or "30001".</summary>
    ModiconOneBased,
}
