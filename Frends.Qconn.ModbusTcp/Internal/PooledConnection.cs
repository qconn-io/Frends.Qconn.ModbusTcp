using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>A single pooled TCP connection to a Modbus device, wrapping the NModbus master.
/// Per-connection semaphore enforces single-request-in-flight semantics (Modbus req/resp is serial per connection).</summary>
internal sealed class PooledConnection : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private int disposed;

    public ConnectionKey Key { get; }
    public TcpClient TcpClient { get; }
    public IModbusMaster Master { get; }
    public long OriginalConnectTimeMs { get; }
    public DateTimeOffset LastUsedUtc { get; private set; }
    public long TotalOperations { get; private set; }
    public long TotalErrors { get; private set; }
    public DateTimeOffset? LastErrorUtc { get; private set; }

    public bool IsDisposed => Volatile.Read(ref disposed) == 1;

    /// <summary>True while a caller holds a lease on this connection (between TryEnterAsync and Release).</summary>
    public bool InUse { get; private set; }

    /// <summary>Idle-eviction budget in milliseconds, snapshotted from the last caller's PoolOptions.IdleTimeoutMs.</summary>
    public int IdleBudgetMs { get; internal set; } = 60_000;

    public PooledConnection(ConnectionKey key, TcpClient tcpClient, IModbusMaster master, long connectTimeMs)
    {
        Key = key;
        TcpClient = tcpClient;
        Master = master;
        OriginalConnectTimeMs = connectTimeMs;
        LastUsedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Tries to enter the per-connection gate within the given budget.
    /// Returns true on success — caller must call Release exactly once.</summary>
    public async Task<bool> TryEnterAsync(int timeoutMs, CancellationToken cancellationToken)
    {
        bool entered = await gate.WaitAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        if (entered) InUse = true;
        return entered;
    }

    /// <summary>Releases the per-connection gate. Updates last-used timestamp and counters.</summary>
    public void Release(bool success)
    {
        LastUsedUtc = DateTimeOffset.UtcNow;
        TotalOperations++;
        if (!success)
        {
            TotalErrors++;
            LastErrorUtc = LastUsedUtc;
        }

        InUse = false;
        gate.Release();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        try
        {
            Master.Dispose();
        }
        catch
        { /* NModbus master disposes the TcpClient */
        }
        try
        {
            TcpClient.Dispose();
        }
        catch
        {
        }
        await Task.CompletedTask;
        gate.Dispose();
    }
}
