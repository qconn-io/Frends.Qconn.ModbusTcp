using System.Threading;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Tests.Helpers;

/// <summary>IPointSource that throws Modbus exception code 6 (SlaveBusy) on the first <paramref name="failCount"/>
/// read or write calls, then delegates to a BoundedSlaveDataStore for subsequent calls.</summary>
internal sealed class FailNTimesPointSource<T> : IPointSource<T>
{
    private readonly IPointSource<T> _inner;
    private int _remaining;

    internal FailNTimesPointSource(IPointSource<T> inner, int failCount)
    {
        _inner = inner;
        _remaining = failCount;
    }

    public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
    {
        MaybeThrow();
        return _inner.ReadPoints(startAddress, numberOfPoints);
    }

    public void WritePoints(ushort startAddress, T[] points)
    {
        MaybeThrow();
        _inner.WritePoints(startAddress, points);
    }

    private void MaybeThrow()
    {
        if (Interlocked.Decrement(ref _remaining) >= 0)
            throw new InvalidModbusRequestException(6); // SlaveBusy — transient, retriable
    }
}

/// <summary>BoundedSlaveDataStore variant that wraps each PointSource in FailNTimesPointSource.</summary>
internal sealed class FailNTimesSlaveDataStore : ISlaveDataStore
{
    public FailNTimesSlaveDataStore(int failCount, ushort maxAddress = 1000)
    {
        var inner = new BoundedSlaveDataStore(maxAddress);
        CoilDiscretes    = new FailNTimesPointSource<bool>(inner.CoilDiscretes, failCount);
        CoilInputs       = new FailNTimesPointSource<bool>(inner.CoilInputs, failCount);
        HoldingRegisters = new FailNTimesPointSource<ushort>(inner.HoldingRegisters, failCount);
        InputRegisters   = new FailNTimesPointSource<ushort>(inner.InputRegisters, failCount);
    }

    public IPointSource<bool>   CoilDiscretes    { get; }
    public IPointSource<bool>   CoilInputs       { get; }
    public IPointSource<ushort> HoldingRegisters { get; }
    public IPointSource<ushort> InputRegisters   { get; }
}

