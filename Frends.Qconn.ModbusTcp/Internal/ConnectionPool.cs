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

/// <summary>Agent-wide static pool of Modbus TCP connections, keyed by (ConnectionKey, slot).
/// Supports up to MaxConnectionsPerDevice connections per device (default 1).
/// One pool per Agent process; survives for the process lifetime, with idle eviction.</summary>
internal static class ConnectionPool
{
    // Key: (ConnectionKey, slot index). Allows MaxConnectionsPerDevice connections per device.
    private static readonly ConcurrentDictionary<(ConnectionKey key, int slot), PooledConnection> connections = new();
    private static readonly Timer evictionTimer;
    private static readonly int maxTotalConnections;
    private static readonly int maxConnectionsPerDeviceEnv; // 0 = env var not set

    static ConnectionPool()
    {
        maxTotalConnections = ReadIntEnv("ModbusTcp.MaxTotalConnections", 200);
        maxConnectionsPerDeviceEnv = ReadIntEnv("ModbusTcp.MaxConnectionsPerDevice", 0); // 0 = not set

        evictionTimer = new Timer(
            _ => EvictIdleConnections(),
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
        ConnectionKey key, IModbusOptions options, CancellationToken cancellationToken)
    {
        if (key.TransportMode == TransportMode.RtuOverTcp)
            throw new NotSupportedException("TransportMode.RtuOverTcp is reserved for a later milestone and not wired in v2.0.");
        if (key.TlsMode != TransportSecurity.None)
            throw new NotSupportedException("TransportSecurity Tls/MutualTls is reserved for a later milestone and not wired in v2.0.");

        if (!options.Pool.UseConnectionPool)
            return await AcquireEphemeralAsync(key, options, cancellationToken).ConfigureAwait(false);

        // Effective per-device cap: option takes priority over env var; env var takes priority over default 1.
        int maxPerDevice = options.Pool.MaxConnectionsPerDevice > 0
            ? options.Pool.MaxConnectionsPerDevice
            : (maxConnectionsPerDeviceEnv > 0 ? maxConnectionsPerDeviceEnv : 1);

        // Pooled path: scan slots to find a free connection or create one up to the cap.
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // --- Step 1: scan for a non-busy existing connection ---
            PooledConnection? candidate = null;
            int nextFreeSlot = -1;

            for (int slot = 0; slot < maxPerDevice; slot++)
            {
                if (connections.TryGetValue((key, slot), out var existing))
                {
                    if (existing.IsDisposed)
                    {
                        // Slot has a disposed connection — clean it up and reuse the slot.
                        connections.TryRemove(
                            new KeyValuePair<(ConnectionKey key, int slot), PooledConnection>((key, slot), existing));
                        nextFreeSlot = slot;
                        continue;
                    }

                    if (!existing.InUse && candidate == null)
                        candidate = existing;
                }
                else if (nextFreeSlot == -1)
                {
                    nextFreeSlot = slot;
                }
            }

            // --- Step 2: try to enter a free connection ---
            if (candidate != null)
            {
                bool entered = await candidate.TryEnterAsync(options.Pool.AcquireTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                if (!entered)
                {
                    Telemetry.BackpressureRejected.Add(1);
                    throw new TimeoutException(
                        $"Acquire timed out after {options.Pool.AcquireTimeoutMs} ms waiting for a connection slot to {key.Host}:{key.Port}/UnitId={key.UnitId}.");
                }

                if (candidate.IsDisposed || !candidate.TcpClient.Connected)
                {
                    candidate.Release(success: false);

                    // Connection died between the scan and the enter; retry.
                    continue;
                }

                candidate.IdleBudgetMs = options.Pool.IdleTimeoutMs;
                return new ModbusLease(candidate, connectTimeMs: 0, fromPool: true);
            }

            // --- Step 3: create a new connection in a free slot ---
            if (nextFreeSlot >= 0)
            {
                if (connections.Count >= maxTotalConnections)
                {
                    Telemetry.BackpressureRejected.Add(1);
                    throw new InvalidOperationException(
                        $"Agent-wide connection cap reached ({maxTotalConnections}). Close idle Processes or raise ModbusTcp.MaxTotalConnections.");
                }

                var (tcp, connectMs) = await ModbusConnection
                    .ConnectAsync(key.Host, key.Port, options.ConnectTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                var master = new ModbusFactory().CreateMaster(tcp);
                master.Transport.ReadTimeout = options.ReadTimeoutMs;
                master.Transport.WriteTimeout = options.ReadTimeoutMs;

                var pooled = new PooledConnection(key, tcp, master, connectMs)
                {
                    IdleBudgetMs = options.Pool.IdleTimeoutMs,
                };
                if (!connections.TryAdd((key, nextFreeSlot), pooled))
                {
                    await pooled.DisposeAsync().ConfigureAwait(false);
                    continue; // Slot was taken by a concurrent caller; retry.
                }

                bool firstEntered = await pooled.TryEnterAsync(options.Pool.AcquireTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                if (!firstEntered)
                {
                    connections.TryRemove(
                        new KeyValuePair<(ConnectionKey key, int slot), PooledConnection>((key, nextFreeSlot), pooled));
                    await pooled.DisposeAsync().ConfigureAwait(false);
                    Telemetry.BackpressureRejected.Add(1);
                    throw new TimeoutException(
                        $"Acquire timed out after {options.Pool.AcquireTimeoutMs} ms on a freshly-created connection to {key.Host}:{key.Port}.");
                }

                return new ModbusLease(pooled, connectTimeMs: connectMs, fromPool: false);
            }

            // --- Step 4: all slots busy; wait on the first non-disposed slot ---
            if (connections.TryGetValue((key, 0), out var firstConn) && !firstConn.IsDisposed)
            {
                bool entered = await firstConn.TryEnterAsync(options.Pool.AcquireTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                if (!entered)
                {
                    Telemetry.BackpressureRejected.Add(1);
                    throw new TimeoutException(
                        $"Acquire timed out after {options.Pool.AcquireTimeoutMs} ms: all {maxPerDevice} connection(s) busy for {key.Host}:{key.Port}/UnitId={key.UnitId}.");
                }

                if (firstConn.IsDisposed || !firstConn.TcpClient.Connected)
                {
                    firstConn.Release(success: false);
                    continue;
                }

                firstConn.IdleBudgetMs = options.Pool.IdleTimeoutMs;
                return new ModbusLease(firstConn, connectTimeMs: 0, fromPool: true);
            }

            // All slots disposed simultaneously — retry from scratch.
        }
    }

    private static async Task<ModbusLease> AcquireEphemeralAsync(
        ConnectionKey key, IModbusOptions options, CancellationToken cancellationToken)
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
        // Search all slots for this connection and remove it.
        foreach (var kv in connections.ToArray())
        {
            if (ReferenceEquals(kv.Value, pooled))
            {
                connections.TryRemove(kv);
                return;
            }
        }
    }

    public static PoolSnapshot Snapshot()
    {
        var connections = ConnectionPool.connections.Values.ToArray();
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
            ActiveConnections: connections.Count(c => !c.IsDisposed && c.InUse),
            IdleConnections: connections.Count(c => !c.IsDisposed && !c.InUse),
            PerDevice: perDevice);
    }

    private static void EvictIdleConnections()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in connections.ToArray())
        {
            var conn = kv.Value;
            var idleBudget = TimeSpan.FromMilliseconds(conn.IdleBudgetMs);
            if (conn.IsDisposed || now - conn.LastUsedUtc > idleBudget)
            {
                if (connections.TryRemove(kv))
                {
                    _ = conn.DisposeAsync().AsTask();
                    Telemetry.ConnectionsEvicted.Add(1);
                }
            }
        }
    }

    private static void DisposeAll()
    {
        evictionTimer.Dispose();
        foreach (var kv in connections.ToArray())
        {
            connections.TryRemove(kv);
            try
            {
                kv.Value.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
            }
        }
    }

    /// <summary>Runs the idle-eviction pass immediately. Intended for test use only.</summary>
    internal static void RunEvictionForTests() => EvictIdleConnections();

    /// <summary>Drains all pooled connections. Intended for test teardown only.</summary>
    internal static void ResetForTests()
    {
        foreach (var kv in connections.ToArray())
        {
            if (connections.TryRemove(kv))
            {
                try
                {
                    kv.Value.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                }
                catch
                {
                }
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
