using System;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Tests.Helpers;
using Xunit;
using ReadTask = Frends.Qconn.ModbusTcp.Read.Read;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Tests that pool options (IdleTimeoutMs, MaxConnectionsPerDevice, Active vs Idle counters) behave correctly (WP3).</summary>
public class PoolBehaviorTests : IDisposable
{
    public PoolBehaviorTests() => ConnectionPool.ResetForTests();
    public void Dispose() => ConnectionPool.ResetForTests();

    private static ConnectionKey MakeKey(int port) =>
        new("127.0.0.1", port, 1, TransportMode.TcpNative, TransportSecurity.None, null, null);

    // ─── IdleTimeoutMs ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Pool_Evicts_Connection_After_IdleTimeoutMs()
    {
        using var slave = new InProcessSlave(1);

        // Seed pool with a very short idle budget.
        var options = new Options { Pool = { IdleTimeoutMs = 200 } };
        var result = await ReadTask.ReadData(
            new Input { Host = "127.0.0.1", Port = slave.Port, UnitId = 1, StartAddress = 0, NumberOfValues = 1 },
            options, CancellationToken.None);
        Assert.True(result.Success);

        Assert.Equal(1, ConnectionPool.Snapshot().TotalConnections);

        // Wait past the idle budget then trigger eviction.
        await Task.Delay(400);
        ConnectionPool.RunEvictionForTests();

        Assert.Equal(0, ConnectionPool.Snapshot().TotalConnections);
    }

    [Fact]
    public async Task Pool_Does_Not_Evict_Connection_Before_IdleTimeoutMs()
    {
        using var slave = new InProcessSlave(1);

        // Use a long idle budget — connection should still be alive right after the read.
        var options = new Options { Pool = { IdleTimeoutMs = 60_000 } };
        var result = await ReadTask.ReadData(
            new Input { Host = "127.0.0.1", Port = slave.Port, UnitId = 1, StartAddress = 0, NumberOfValues = 1 },
            options, CancellationToken.None);
        Assert.True(result.Success);

        ConnectionPool.RunEvictionForTests();

        Assert.Equal(1, ConnectionPool.Snapshot().TotalConnections);
    }

    // ─── MaxConnectionsPerDevice ────────────────────────────────────────────────

    [Fact]
    public async Task Pool_Allows_Two_Concurrent_Leases_When_MaxPerDevice_Is_2()
    {
        using var slave = new InProcessSlave(1);
        var key = MakeKey(slave.Port);
        var options = new Options { Pool = { MaxConnectionsPerDevice = 2, AcquireTimeoutMs = 2000 } };

        // Acquire two leases — both should succeed since cap = 2.
        var lease1 = await ConnectionPool.AcquireAsync(key, options, CancellationToken.None);
        var lease2 = await ConnectionPool.AcquireAsync(key, options, CancellationToken.None);

        var snapshot = ConnectionPool.Snapshot();
        Assert.Equal(2, snapshot.ActiveConnections);

        await lease1.DisposeAsync();
        await lease2.DisposeAsync();
    }

    [Fact]
    public async Task Pool_Blocks_Third_Lease_When_MaxPerDevice_Is_2_And_Both_Busy()
    {
        using var slave = new InProcessSlave(1);
        var key = MakeKey(slave.Port);
        // Two slots, short acquire timeout so the third attempt fails fast.
        var options = new Options { Pool = { MaxConnectionsPerDevice = 2, AcquireTimeoutMs = 150 } };

        var lease1 = await ConnectionPool.AcquireAsync(key, options, CancellationToken.None);
        var lease2 = await ConnectionPool.AcquireAsync(key, options, CancellationToken.None);

        // Both slots are InUse — a third acquire must time out.
        await Assert.ThrowsAsync<TimeoutException>(
            () => ConnectionPool.AcquireAsync(key, options, CancellationToken.None));

        await lease1.DisposeAsync();
        await lease2.DisposeAsync();
    }

    // ─── Active vs Idle counters ────────────────────────────────────────────────

    [Fact]
    public async Task Snapshot_Reports_Active_And_Idle_Counters_Correctly()
    {
        using var slave = new InProcessSlave(1);
        var key = MakeKey(slave.Port);
        var options = new Options { Pool = { MaxConnectionsPerDevice = 2, AcquireTimeoutMs = 2000 } };

        // Seed two pooled connections.
        var lease1 = await ConnectionPool.AcquireAsync(key, options, CancellationToken.None);
        var lease2 = await ConnectionPool.AcquireAsync(key, options, CancellationToken.None);

        // Release one — it becomes Idle; the other stays Active.
        await lease2.DisposeAsync();

        var snapshot = ConnectionPool.Snapshot();
        Assert.Equal(1, snapshot.ActiveConnections);
        Assert.Equal(1, snapshot.IdleConnections);
        Assert.Equal(2, snapshot.TotalConnections);

        await lease1.DisposeAsync();
    }
}
