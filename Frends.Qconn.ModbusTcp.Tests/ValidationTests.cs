using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using ReadTask = Frends.Qconn.ModbusTcp.Read.Read;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Test 1: Input validation — zero counts, register-count limits per function code.</summary>
public class ValidationTests
{
    private static Input DefaultInput() => new()
    {
        Host = "127.0.0.1",
        Port = 502,
        UnitId = 1,
        DataType = ModbusDataType.HoldingRegisters,
        StartAddress = 0,
        NumberOfValues = 1,
        ValueType = ModbusValueType.Raw,
    };

    private static Options DefaultOptions() => new();

    [Fact]
    public async Task NumberOfValues_Zero_Returns_InvalidInput()
    {
        var input = DefaultInput();
        input.NumberOfValues = 0;

        var result = await ReadTask.ReadData(input, DefaultOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.InvalidInput, result.Error!.Category);
        Assert.False(result.Error.IsTransient);
    }

    [Fact]
    public async Task HoldingRegisters_Over_125_Returns_InvalidInput()
    {
        var input = DefaultInput();
        input.DataType = ModbusDataType.HoldingRegisters;
        input.ValueType = ModbusValueType.Raw;
        input.NumberOfValues = 126;

        var result = await ReadTask.ReadData(input, DefaultOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.InvalidInput, result.Error!.Category);
    }

    [Fact]
    public async Task Float32_63_Values_Exceeds_Register_Limit()
    {
        // 63 Float32 = 126 registers > 125 limit
        var input = DefaultInput();
        input.DataType = ModbusDataType.HoldingRegisters;
        input.ValueType = ModbusValueType.Float32;
        input.NumberOfValues = 63;

        var result = await ReadTask.ReadData(input, DefaultOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.InvalidInput, result.Error!.Category);
    }

    [Fact]
    public async Task InputRegisters_Over_125_Returns_InvalidInput()
    {
        var input = DefaultInput();
        input.DataType = ModbusDataType.InputRegisters;
        input.NumberOfValues = 200;

        var result = await ReadTask.ReadData(input, DefaultOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.InvalidInput, result.Error!.Category);
    }

    [Fact]
    public async Task Coils_Over_2000_Returns_InvalidInput()
    {
        var input = DefaultInput();
        input.DataType = ModbusDataType.Coils;
        input.NumberOfValues = 2001;

        var result = await ReadTask.ReadData(input, DefaultOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.InvalidInput, result.Error!.Category);
    }

    [Fact]
    public async Task DiscreteInputs_Over_2000_Returns_InvalidInput()
    {
        var input = DefaultInput();
        input.DataType = ModbusDataType.DiscreteInputs;
        input.NumberOfValues = 2001;

        var result = await ReadTask.ReadData(input, DefaultOptions(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.InvalidInput, result.Error!.Category);
    }
}
