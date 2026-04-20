using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Identity of a pooled Modbus TCP connection. Two Tasks using the same ConnectionKey
/// share the same pooled socket. Also the key under which the circuit breaker is registered.</summary>
/// <remarks>TLS fields are carried now so v2.1 can introduce TLS without changing the pool contract.</remarks>
internal sealed record ConnectionKey(
    string Host,
    int Port,
    byte UnitId,
    TransportMode TransportMode,
    TransportSecurity TlsMode,
    string? ClientCertThumbprint,
    string? ExpectedServerCertThumbprint);
