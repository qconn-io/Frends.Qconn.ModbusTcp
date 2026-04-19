namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Timing and connection metrics for the read operation. Always populated, including on failure.</summary>
public class Diagnostics
{
    /// <summary>Time spent establishing the TCP connection, in milliseconds.</summary>
    public long ConnectTimeMs { get; }

    /// <summary>Time spent waiting for the Modbus response after the connection was established, in milliseconds.</summary>
    public long ReadTimeMs { get; }

    /// <summary>Total wall-clock time for the operation, in milliseconds.</summary>
    public long TotalTimeMs { get; }

    /// <summary>Number of read attempts made. Always 1 in v1.0 — placeholder for v1.1 retry support.</summary>
    public int AttemptCount { get; }

    /// <summary>Hostname or IP address used for the connection.</summary>
    public string Host { get; }

    /// <summary>TCP port used for the connection.</summary>
    public int Port { get; }

    /// <summary>Modbus unit ID sent in the request.</summary>
    public byte UnitId { get; }

    /// <summary>Start address as sent on the wire, after AddressingMode translation.</summary>
    public ushort WireStartAddress { get; }

    /// <summary>Number of registers requested on the wire. For multi-word types, this is greater than NumberOfValues.</summary>
    public ushort WireRegisterCount { get; }

    /// <summary>Initializes a new Diagnostics instance.</summary>
    public Diagnostics(
        long connectTimeMs, long readTimeMs, long totalTimeMs,
        string host, int port, byte unitId,
        ushort wireStartAddress, ushort wireRegisterCount,
        int attemptCount = 1)
    {
        ConnectTimeMs = connectTimeMs;
        ReadTimeMs = readTimeMs;
        TotalTimeMs = totalTimeMs;
        Host = host;
        Port = port;
        UnitId = unitId;
        WireStartAddress = wireStartAddress;
        WireRegisterCount = wireRegisterCount;
        AttemptCount = attemptCount;
    }
}
