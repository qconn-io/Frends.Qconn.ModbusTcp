using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Properties the connection pool and session layer need from either Options or WriteOptions.</summary>
internal interface IModbusOptions
{
    int ConnectTimeoutMs { get; }
    int ReadTimeoutMs { get; }
    PoolOptions Pool { get; }
}
