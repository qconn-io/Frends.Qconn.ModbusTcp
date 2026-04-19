using System;
using System.Net;
using System.Net.Sockets;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Tests.Helpers;

/// <summary>Hosts an in-process NModbus TCP slave for integration tests.
/// Listens on 127.0.0.1 with a random port. Dispose to shut down.</summary>
internal sealed class InProcessSlave : IDisposable
{
    private readonly TcpListener _listener;
    private readonly IModbusSlaveNetwork _network;
    private bool _disposed;

    /// <summary>The auto-assigned TCP port the slave is listening on.</summary>
    public int Port { get; }

    /// <summary>The slave data store — use to pre-populate holding registers, coils, etc.</summary>
    public ISlaveDataStore DataStore { get; }

    public InProcessSlave(byte unitId = 1)
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        var factory = new ModbusFactory();
        // BoundedSlaveDataStore throws Modbus exception 2 for addresses >= 1000,
        // enabling exception-path tests while keeping happy-path tests working.
        var dataStore = new BoundedSlaveDataStore(maxAddress: 1000);
        var slave = factory.CreateSlave(unitId, dataStore);
        DataStore = slave.DataStore;

        _network = factory.CreateSlaveNetwork(_listener);
        _network.AddSlave(slave);

        // Fire-and-forget: the network listens until disposed.
        _ = _network.ListenAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _network.Dispose();
        _listener.Stop();
    }
}
