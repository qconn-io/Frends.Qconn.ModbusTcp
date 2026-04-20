using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Tests.Helpers;
using Frends.Qconn.ModbusTcp.Write;
using Frends.Qconn.ModbusTcp.Write.Definitions;
using Xunit;
using ReadTask = Frends.Qconn.ModbusTcp.Read.Read;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Round-trip write → read correctness tests against the in-process NModbus slave.
/// Verifies ModbusEncoder mirrors ModbusDecoder for each ValueType × ByteOrder combination.</summary>
public class WriteRoundTripTests
{
    [Theory]
    [InlineData(ByteOrder.BigEndian)]
    [InlineData(ByteOrder.LittleEndian)]
    [InlineData(ByteOrder.BigEndianByteSwap)]
    [InlineData(ByteOrder.LittleEndianWordSwap)]
    public async Task Float32_RoundTrip_All_ByteOrders(ByteOrder order)
    {
        using var slave = new InProcessSlave();
        var options = new Options { ByteOrder = order };
        var writeOptions = new WriteOptions { ByteOrder = order };

        var writeInput = new WriteInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 10,
            NumberOfValues = 1,
            ValueType = ModbusValueType.Float32,
            Values = new[] { 123.456f },
        };

        var writeResult = await WriteMultiple.WriteData(writeInput, writeOptions, CancellationToken.None);
        Assert.True(writeResult.Success);

        var readInput = new Input
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 10,
            NumberOfValues = 1,
            ValueType = ModbusValueType.Float32,
        };
        var readResult = await ReadTask.ReadData(readInput, options, CancellationToken.None);
        Assert.True(readResult.Success);
        var floats = Assert.IsType<float[]>(readResult.Data);
        Assert.Equal(123.456f, floats[0], precision: 3);
    }

    [Theory]
    [InlineData(ByteOrder.BigEndian)]
    [InlineData(ByteOrder.LittleEndian)]
    [InlineData(ByteOrder.LittleEndianWordSwap)]
    public async Task Int32_RoundTrip(ByteOrder order)
    {
        using var slave = new InProcessSlave();
        var options = new Options { ByteOrder = order };
        var writeOptions = new WriteOptions { ByteOrder = order };

        var writeInput = new WriteInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 20,
            NumberOfValues = 1,
            ValueType = ModbusValueType.Int32,
            Values = new[] { -123456 },
        };

        await WriteMultiple.WriteData(writeInput, writeOptions, CancellationToken.None);

        var readResult = await ReadTask.ReadData(new Input
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 20,
            NumberOfValues = 1,
            ValueType = ModbusValueType.Int32,
        }, options, CancellationToken.None);

        var ints = Assert.IsType<int[]>(readResult.Data);
        Assert.Equal(-123456, ints[0]);
    }

    [Fact]
    public async Task UInt16_Single_Register_RoundTrip()
    {
        using var slave = new InProcessSlave();
        var input = new WriteSingleRegisterInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            StartAddress = 0,
            ValueType = ModbusValueType.UInt16,
            Value = (ushort)42000,
        };
        await WriteSingleRegister.WriteData(input, new WriteOptions(), CancellationToken.None);

        var readResult = await ReadTask.ReadData(new Input
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 0,
            NumberOfValues = 1,
            ValueType = ModbusValueType.UInt16,
        }, new Options(), CancellationToken.None);

        var regs = Assert.IsType<ushort[]>(readResult.Data);
        Assert.Equal((ushort)42000, regs[0]);
    }

    [Fact]
    public async Task Coil_Write_Read_Boolean()
    {
        using var slave = new InProcessSlave();
        await WriteSingleCoil.WriteData(new WriteSingleCoilInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            StartAddress = 5,
            Value = true,
        }, new WriteOptions(), CancellationToken.None);

        var readResult = await ReadTask.ReadData(new Input
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.Coils,
            StartAddress = 5,
            NumberOfValues = 1,
        }, new Options(), CancellationToken.None);

        var bools = Assert.IsType<bool[]>(readResult.Data);
        Assert.True(bools[0]);
    }

    [Fact]
    public async Task AsciiString_Write_Read_RoundTrip()
    {
        using var slave = new InProcessSlave();
        var writeInput = new WriteInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 30,
            NumberOfValues = 5, // 5 registers = 10 bytes
            ValueType = ModbusValueType.AsciiString,
            Values = "Hello",
        };
        await WriteMultiple.WriteData(writeInput, new WriteOptions(), CancellationToken.None);

        var readResult = await ReadTask.ReadData(new Input
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 30,
            NumberOfValues = 5,
            ValueType = ModbusValueType.AsciiString,
        }, new Options(), CancellationToken.None);

        Assert.Equal("Hello", readResult.Data);
    }

    [Fact]
    public async Task Int32_Array_RoundTrip()
    {
        using var slave = new InProcessSlave();
        var writeInput = new WriteInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 40,
            NumberOfValues = 3,
            ValueType = ModbusValueType.Int32,
            Values = new[] { 100_000, -200_000, 300_000 },
        };
        await WriteMultiple.WriteData(writeInput, new WriteOptions(), CancellationToken.None);

        var readResult = await ReadTask.ReadData(new Input
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 40,
            NumberOfValues = 3,
            ValueType = ModbusValueType.Int32,
        }, new Options(), CancellationToken.None);

        var ints = Assert.IsType<int[]>(readResult.Data);
        Assert.Equal(new[] { 100_000, -200_000, 300_000 }, ints);
    }

    [Fact]
    public async Task Float64_RoundTrip_BigEndian()
    {
        using var slave = new InProcessSlave();
        var writeInput = new WriteInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 50,
            NumberOfValues = 1,
            ValueType = ModbusValueType.Float64,
            Values = new[] { 1234567.89 },
        };
        await WriteMultiple.WriteData(writeInput, new WriteOptions(), CancellationToken.None);

        var readResult = await ReadTask.ReadData(new Input
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 50,
            NumberOfValues = 1,
            ValueType = ModbusValueType.Float64,
        }, new Options(), CancellationToken.None);

        var doubles = Assert.IsType<double[]>(readResult.Data);
        Assert.Equal(1234567.89, doubles[0], precision: 2);
    }
}
