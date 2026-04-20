using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Agent-wide static pool of Modbus TCP connections, keyed by ConnectionKey.
/// One pool per Agent process; survives for the process lifetime, with idle eviction.</summary>
internal static class ConnectionPool
{
    private static readonly ConcurrentDictionary<ConnectionKey, PooledConnection> _connections = new();
    private static readonly Timer _evictionTimer;
    private static readonly int _maxTotalConnections;
    private static readonly int _maxConnectionsPerDevice;

    static ConnectionPool()
    {
        _maxTotalConnections = ReadIntEnv("ModbusTcp.MaxTotalConnections", 200);
        _maxConnectionsPerDevice = ReadIntEnv("ModbusTcp.MaxConnectionsPerDevice", 1);

        _evictionTimer = new Timer(_ => EvictIdleConnections(),
            state: null,
            dueTime: TimeSpan.FromSeconds(30),
            period: TimeSpan.FromSeconds(30));

        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeAll();
        var alc = AssemblyLoadContext.GetLoadContext(typeof(ConnectionPool).Assembly);
        if (alc != null) alc.Unloading += _ => DisposeAll();
    }

    /// <summary>Acquires a lease to a Modbus master. If <c>UseConnectionPool</c> is false, a fresh ephemeral
    /// connection is created and disposed on lease release. Otherwise, the pool is consulted.</summary>
    public static async Task<ModbusLease> AcquireAsync(
        ConnectionKey key, Options options, CancellationToken cancellationToken)
    {
        if (key.TransportMode == TransportMode.RtuOverTcp)
            throw new NotSupportedException("TransportMode.RtuOverTcp is reserved for a later milestone and not wired in v2.0.");
        if (key.TlsMode != TransportSecurity.None)
            throw new NotSupportedException("TransportSecurity Tls/MutualTls is reserved for a later milestone and not wired in v2.0.");

        if (!options.Pool.UseConnectionPool)
            return await AcquireEphemeralAsync(key, options, cancellationToken).ConfigureAwait(false);

        // Pooled path: loop to tolerate race where a stale/disposed connection is evicted between get and TryEnter.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = _connections.TryGetValue(key, out var candidate) ? candidate : null;
            if (existing != null && !existing.IsDisposed)
            {
                bool entered = await existing.TryEnterAsync(options.Pool.AcquireTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                if (!entered)
                {
                    Telemetry.BackpressureRejected.Add(1);
                    throw new TimeoutException(
                        $"Acquire timed out after {options.Pool.AcquireTimeoutMs} ms waiting for a connection slot to {key.Host}:{key.Port}/UnitId={key.UnitId}.");
                }

                if (existing.IsDisposed || !existing.TcpClient.Connected)
                {
                    existing.Release(success: false);
                    _connections.TryRemove(new KeyValuePair<ConnectionKey, PooledConnection>(key, existing));
                    _ = existing.DisposeAsync().AsTask();
                    continue;
                }

                return new ModbusLease(existing, connectTimeMs: 0, fromPool: true);
            }

            if (_connections.Count >= _maxTotalConnections)
            {
                Telemetry.BackpressureRejected.Add(1);
                throw new InvalidOperationException(
                    $"Agent-wide connection cap reached ({_maxTotalConnections}). Close idle Processes or raise ModbusTcp.MaxTotalConnections.");
            }

            var (tcp, connectMs) = await ModbusConnection
                .ConnectAsync(key.Host, key.Port, options.ConnectTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
            var master = new ModbusFactory().CreateMaster(tcp);
            master.Transport.ReadTimeout = options.ReadTimeoutMs;
            master.Transport.WriteTimeout = options.ReadTimeoutMs;

            var pooled = new PooledConnection(key, tcp, master, connectMs);
            if (!_connections.TryAdd(key, pooled))
            {
                await pooled.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            bool firstEntered = await pooled.TryEnterAsync(options.Pool.AcquireTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
            if (!firstEntered)
            {
                _connections.TryRemove(new KeyValuePair<ConnectionKey, PooledConnection>(key, pooled));
                await pooled.DisposeAsync().ConfigureAwait(false);
                Telemetry.BackpressureRejected.Add(1);
                throw new TimeoutException(
                    $"Acquire timed out after {options.Pool.AcquireTimeoutMs} ms on a freshly-created connection to {key.Host}:{key.Port}.");
            }
            return new ModbusLease(pooled, connectTimeMs: connectMs, fromPool: false);
        }
    }

    private static async Task<ModbusLease> AcquireEphemeralAsync(
        ConnectionKey key, Options options, CancellationToken cancellationToken)
    {
        var (tcp, connectMs) = await ModbusConnection
            .ConnectAsync(key.Host, key.Port, options.ConnectTimeoutMs, cancellationToken)
            .ConfigureAwait(false);
        var master = new ModbusFactory().CreateMaster(tcp);
        master.Transport.ReadTimeout = options.ReadTimeoutMs;
        master.Transport.WriteTimeout = options.ReadTimeoutMs;
        var pooled = new PooledConnection(key, tcp, master, connectMs);
        await pooled.TryEnterAsync(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        return new ModbusLease(pooled, connectTimeMs: connectMs, fromPool: false, ephemeral: true);
    }

    internal static void Drop(PooledConnection pooled)
    {
        _connections.TryRemove(new KeyValuePair<ConnectionKey, PooledConnection>(pooled.Key, pooled));
    }

    public static PoolSnapshot Snapshot()
    {
        var connections = _connections.Values.ToArray();
        var perDevice = connections
            .GroupBy(c => (c.Key.Host, c.Key.Port, c.Key.UnitId))
            .Select(g => new PerDeviceStats(
                g.Key.Host, g.Key.Port, g.Key.UnitId,
                g.Count(),
                g.Sum(c => c.TotalOperations),
                g.Sum(c => c.TotalErrors),
                g.Max(c => c.LastUsedUtc)))
            .ToArray();
        return new PoolSnapshot(
            TotalConnections: connections.Length,
            ActiveConnections: connections.Count(c => !c.IsDisposed),
            IdleConnections: connections.Count(c => !c.IsDisposed),
            PerDevice: perDevice);
    }

    private static void EvictIdleConnections()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, conn) in _connections.ToArray())
        {
            // Options.IdleTimeoutMs is per-call; for eviction use a conservative default 60s
            // since the pool is Agent-global and each connection may have been acquired with different options.
            var idleBudget = TimeSpan.FromMilliseconds(60_000);
            if (conn.IsDisposed || now - conn.LastUsedUtc > idleBudget)
            {
                if (_connections.TryRemove(new KeyValuePair<ConnectionKey, PooledConnection>(key, conn)))
                {
                    _ = conn.DisposeAsync().AsTask();
                    Telemetry.ConnectionsEvicted.Add(1);
                }
            }
        }
    }

    private static void DisposeAll()
    {
        _evictionTimer.Dispose();
        foreach (var kv in _connections.ToArray())
        {
            _connections.TryRemove(kv);
            try { kv.Value.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    /// <summary>Drains all pooled connections. Intended for test teardown only.</summary>
    internal static void ResetForTests()
    {
        foreach (var kv in _connections.ToArray())
        {
            if (_connections.TryRemove(kv))
            {
                try { kv.Value.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5)); } catch { }
            }
        }
    }

    private static int ReadIntEnv(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}

/// <summary>Snapshot of pool state for the PoolStatistics admin Task.</summary>
internal sealed record PoolSnapshot(
    int TotalConnections,
    int ActiveConnections,
    int IdleConnections,
    PerDeviceStats[] PerDevice);

internal sealed record PerDeviceStats(
    string Host,
    int Port,
    byte UnitId,
    int Connections,
    long TotalOperations,
    long TotalErrors,
    DateTimeOffset LastUsedUtc);
