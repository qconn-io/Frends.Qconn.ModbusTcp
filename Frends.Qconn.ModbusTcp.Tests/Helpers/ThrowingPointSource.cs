using System;
using System.Collections.Generic;
using NModbus;

namespace Frends.Qconn.ModbusTcp.Tests.Helpers;

/// <summary>IPointSource implementation that throws Modbus exception code 2 (IllegalDataAddress)
/// for any read request whose start address exceeds maxAddress.</summary>
internal sealed class ThrowingPointSource<T> : IPointSource<T>
{
    private readonly ushort _maxAddress;
    private readonly Dictionary<ushort, T> _data = new();

    internal ThrowingPointSource(ushort maxAddress) => _maxAddress = maxAddress;

    public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
    {
        if (startAddress >= _maxAddress || startAddress + numberOfPoints > _maxAddress)
            throw new InvalidModbusRequestException(2); // IllegalDataAddress

        var result = new T[numberOfPoints];
        for (ushort i = 0; i < numberOfPoints; i++)
        {
            ushort addr = (ushort)(startAddress + i);
            if (_data.TryGetValue(addr, out var val))
                result[i] = val;
        }
        return result;
    }

    public void WritePoints(ushort startAddress, T[] points)
    {
        for (int i = 0; i < points.Length; i++)
            _data[(ushort)(startAddress + i)] = points[i];
    }
}

/// <summary>ISlaveDataStore using ThrowingPointSource for all sources, so reads beyond maxAddress
/// cause the slave to return Modbus exception code 2 to the client.</summary>
internal sealed class BoundedSlaveDataStore : ISlaveDataStore
{
    public BoundedSlaveDataStore(ushort maxAddress = 1000)
    {
        CoilDiscretes   = new ThrowingPointSource<bool>(maxAddress);
        CoilInputs      = new ThrowingPointSource<bool>(maxAddress);
        HoldingRegisters = new ThrowingPointSource<ushort>(maxAddress);
        InputRegisters  = new ThrowingPointSource<ushort>(maxAddress);
    }

    public IPointSource<bool>   CoilDiscretes    { get; }
    public IPointSource<bool>   CoilInputs       { get; }
    public IPointSource<ushort> HoldingRegisters { get; }
    public IPointSource<ushort> InputRegisters   { get; }
}
