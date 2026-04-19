using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.ReadBatch.Definitions;
using Frends.Qconn.ModbusTcp.Tests.Helpers;
using ReadTask = Frends.Qconn.ModbusTcp.Read.Read;
using ReadBatchTask = Frends.Qconn.ModbusTcp.ReadBatch.ReadBatch;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Tests 6, 8, 11: Happy path against in-process NModbus slave, slave exception, batch partial failure.</summary>
public class IntegrationTests
{
    private static Input MakeInput(int port, ModbusDataType dt, ushort addr, ushort count, ModbusValueType vt = ModbusValueType.Raw) =>
        new() { Host = "127.0.0.1", Port = port, UnitId = 1, DataType = dt, StartAddress = addr, NumberOfValues = count, ValueType = vt };

    private static Options DefaultOptions() => new();

    // ─── Test 6: Happy path ───────────────────────────────────────────────────

    [Fact]
    public async Task HoldingRegisters_Raw_Returns_Correct_Ushort_Array()
    {
        using var slave = new InProcessSlave(1);
        slave.DataStore.HoldingRegisters.WritePoints(0, new ushort[] { 0x1234, 0x5678 });

        var result = await ReadTask.ReadData(
            MakeInput(slave.Port, ModbusDataType.HoldingRegisters, 0, 2, ModbusValueType.Raw),
            DefaultOptions(), CancellationToken.None);

        Assert.True(result.Success);
        var arr = Assert.IsType<ushort[]>(result.Data);
        Assert.Equal(new ushort[] { 0x1234, 0x5678 }, arr);
    }

    [Fact]
    public async Task HoldingRegisters_Float32_BigEndian_Returns_Correct_Float()
    {
        using var slave = new InProcessSlave(1);
        // 123456.0f BigEndian: reg0=0x47F1, reg1=0x2000
        slave.DataStore.HoldingRegisters.WritePoints(0, new ushort[] { 0x47F1, 0x2000 });

        var options = new Options { ByteOrder = ByteOrder.BigEndian };
        var result = await ReadTask.ReadData(
            MakeInput(slave.Port, ModbusDataType.HoldingRegisters, 0, 1, ModbusValueType.Float32),
            options, CancellationToken.None);

        Assert.True(result.Success);
        var arr = Assert.IsType<float[]>(result.Data);
        Assert.Equal(123456.0f, arr[0], precision: 0);
        Assert.Equal(123456.0f, Assert.IsType<float>(result.FirstValue), precision: 0);
    }

    [Fact]
    public async Task HoldingRegisters_Int16_Returns_Correct_Short()
    {
        using var slave = new InProcessSlave(1);
        slave.DataStore.HoldingRegisters.WritePoints(0, new ushort[] { 0xFFFF }); // -1 as int16

        var result = await ReadTask.ReadData(
            MakeInput(slave.Port, ModbusDataType.HoldingRegisters, 0, 1, ModbusValueType.Int16),
            DefaultOptions(), CancellationToken.None);

        Assert.True(result.Success);
        var arr = Assert.IsType<short[]>(result.Data);
        Assert.Equal((short)-1, arr[0]);
        Assert.NotNull(result.RawRegisters);
    }

    [Fact]
    public async Task InputRegisters_Raw_Returns_Correct_Value()
    {
        using var slave = new InProcessSlave(1);
        slave.DataStore.InputRegisters.WritePoints(0, new ushort[] { 0xABCD });

        var result = await ReadTask.ReadData(
            MakeInput(slave.Port, ModbusDataType.InputRegisters, 0, 1, ModbusValueType.Raw),
            DefaultOptions(), CancellationToken.None);

        Assert.True(result.Success);
        var arr = Assert.IsType<ushort[]>(result.Data);
        Assert.Equal(0xABCD, arr[0]);
    }

    [Fact]
    public async Task Coils_Returns_Bool_Array()
    {
        using var slave = new InProcessSlave(1);
        slave.DataStore.CoilDiscretes.WritePoints(0, new bool[] { true, false, true });

        var result = await ReadTask.ReadData(
            MakeInput(slave.Port, ModbusDataType.Coils, 0, 3),
            DefaultOptions(), CancellationToken.None);

        Assert.True(result.Success);
        var arr = Assert.IsType<bool[]>(result.Data);
        Assert.Equal(new[] { true, false, true }, arr);
        Assert.Null(result.RawRegisters);
    }

    [Fact]
    public async Task DiscreteInputs_Returns_Bool_Array()
    {
        using var slave = new InProcessSlave(1);
        // NModbus 3.x: discrete inputs are CoilInputs on the ISlaveDataStore
        slave.DataStore.CoilInputs.WritePoints(0, new bool[] { false, true });

        var result = await ReadTask.ReadData(
            MakeInput(slave.Port, ModbusDataType.DiscreteInputs, 0, 2),
            DefaultOptions(), CancellationToken.None);

        Assert.True(result.Success);
        var arr = Assert.IsType<bool[]>(result.Data);
        Assert.Equal(new[] { false, true }, arr);
    }

    [Fact]
    public async Task Result_Diagnostics_Are_Populated()
    {
        using var slave = new InProcessSlave(1);
        slave.DataStore.HoldingRegisters.WritePoints(0, new ushort[] { 42 });

        var result = await ReadTask.ReadData(
            MakeInput(slave.Port, ModbusDataType.HoldingRegisters, 0, 1),
            DefaultOptions(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("127.0.0.1", result.Diagnostics.Host);
        Assert.Equal(slave.Port, result.Diagnostics.Port);
        Assert.Equal(1, result.Diagnostics.UnitId);
        Assert.Equal((ushort)0, result.Diagnostics.WireStartAddress);
        Assert.Equal((ushort)1, result.Diagnostics.WireRegisterCount);
        Assert.True(result.Diagnostics.TotalTimeMs >= 0);
    }

    // ─── Test 8: Slave exception ──────────────────────────────────────────────

    [Fact]
    public async Task SlaveException_IllegalDataAddress_Returns_ModbusException()
    {
        using var slave = new InProcessSlave(1);

        // Read from an address far beyond what the slave has allocated → triggers exception code 2
        var result = await ReadTask.ReadData(
            new Input
            {
                Host = "127.0.0.1",
                Port = slave.Port,
                UnitId = 1,
                DataType = ModbusDataType.HoldingRegisters,
                StartAddress = 60000,
                NumberOfValues = 10,
                ValueType = ModbusValueType.Raw,
            },
            DefaultOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.ModbusException, result.Error!.Category);
        Assert.NotNull(result.Error.ModbusExceptionCode);
        Assert.False(result.Error.IsTransient);
    }

    // ─── Test 11: Batch partial failure ──────────────────────────────────────

    [Fact]
    public async Task Batch_OneItemFails_OthersSucceed_Overall_Success_True()
    {
        using var slave = new InProcessSlave(1);
        slave.DataStore.HoldingRegisters.WritePoints(0, new ushort[] { 0x1234 });

        var batchInput = new BatchInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            Items =
            [
                new BatchReadItem
                {
                    Name = "good",
                    DataType = ModbusDataType.HoldingRegisters,
                    StartAddress = 0,
                    NumberOfValues = 1,
                    ValueType = ModbusValueType.Raw,
                },
                new BatchReadItem
                {
                    Name = "bad",
                    DataType = ModbusDataType.HoldingRegisters,
                    StartAddress = 60000,
                    NumberOfValues = 10,
                    ValueType = ModbusValueType.Raw,
                },
            ],
        };

        var batchResult = await ReadBatchTask.ReadBatchData(batchInput, DefaultOptions(), CancellationToken.None);

        Assert.True(batchResult.Success);
        Assert.Contains("good", batchResult.Items);
        Assert.Contains("bad", batchResult.Items);

        Assert.True(batchResult.Items["good"].Success);
        var goodArr = Assert.IsType<ushort[]>(batchResult.Items["good"].Data);
        Assert.Equal(0x1234, goodArr[0]);

        Assert.False(batchResult.Items["bad"].Success);
        Assert.Equal(ErrorCategory.ModbusException, batchResult.Items["bad"].Error!.Category);
    }

    [Fact]
    public async Task Batch_AllItems_Succeed_Returns_All_Data()
    {
        using var slave = new InProcessSlave(1);
        slave.DataStore.HoldingRegisters.WritePoints(0, new ushort[] { 100, 200 });

        var batchInput = new BatchInput
        {
            Host = "127.0.0.1",
            Port = slave.Port,
            UnitId = 1,
            Items =
            [
                new BatchReadItem { Name = "r1", DataType = ModbusDataType.HoldingRegisters, StartAddress = 0, NumberOfValues = 1, ValueType = ModbusValueType.Raw },
                new BatchReadItem { Name = "r2", DataType = ModbusDataType.HoldingRegisters, StartAddress = 1, NumberOfValues = 1, ValueType = ModbusValueType.Raw },
            ],
        };

        var batchResult = await ReadBatchTask.ReadBatchData(batchInput, DefaultOptions(), CancellationToken.None);

        Assert.True(batchResult.Success);
        Assert.True(batchResult.Items["r1"].Success);
        Assert.True(batchResult.Items["r2"].Success);
    }
}
