using System;
using System.Threading.Tasks;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>A short-lived lease on a pooled Modbus connection. Dispose to release.
/// Call Poison() before disposing if the connection's socket state is no longer trustworthy
/// (e.g. cancellation or SocketException mid-op) — the connection is then closed rather than returned to the pool.</summary>
internal sealed class ModbusLease : IAsyncDisposable
{
    private readonly PooledConnection _connection;
    private readonly bool _ephemeral;
    private bool _poisoned;
    private bool _disposed;

    public IModbusMaster Master => _connection.Master;

    /// <summary>Time spent opening the TCP connection, in milliseconds. Zero on pool hit.</summary>
    public long ConnectTimeMs { get; }

    /// <summary>True if this lease was served from an existing pooled connection.</summary>
    public bool FromPool { get; }

    internal ModbusLease(PooledConnection connection, long connectTimeMs, bool fromPool, bool ephemeral = false)
    {
        _connection = connection;
        _ephemeral = ephemeral;
        ConnectTimeMs = connectTimeMs;
        FromPool = fromPool;
    }

    /// <summary>Mark this lease's connection unreusable. On dispose, the connection is closed and dropped from the pool.</summary>
    public void Poison() => _poisoned = true;

    /// <summary>Disposes the underlying TcpClient and marks the lease poisoned. Used by callers that need to unstick
    /// a pending NModbus read when their CancellationToken fires — disposing the socket is the only way to break
    /// NModbus's synchronous read loop. Subsequent DisposeAsync will drop the connection from the pool.</summary>
    public void DisposeUnderlyingSocketForCancellation()
    {
        _poisoned = true;
        try { _connection.TcpClient.Dispose(); } catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        bool success = !_poisoned;
        _connection.Release(success);
        if (_ephemeral || _poisoned)
        {
            ConnectionPool.Drop(_connection);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
