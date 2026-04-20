using System;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Write;
using Frends.Qconn.ModbusTcp.Write.Definitions;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Verifies WriteGuard blocks writes when WriteOptions.AllowWrites is false, before any socket is opened.</summary>
public class WriteGuardTests
{
    [Fact]
    public void AllowWrites_True_Does_Not_Throw()
    {
        WriteGuard.EnsureAllowed(allowWrites: true);
    }

    [Fact]
    public void AllowWrites_False_Throws_InvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => WriteGuard.EnsureAllowed(allowWrites: false));
        Assert.Contains("AllowWrites = false", ex.Message);
        Assert.Contains("#env.Modbus.AllowWrites", ex.Message);
    }

    [Fact]
    public void Default_WriteOptions_AllowWrites_Is_True()
    {
        var options = new WriteOptions();
        Assert.True(options.AllowWrites);
    }

    [Fact]
    public async Task WriteSingleCoil_Throws_When_AllowWrites_False_Without_Opening_Socket()
    {
        var input = new WriteSingleCoilInput
        {
            // Non-routable port — if WriteGuard fails to block, the connect attempt would hang or fail differently
            Host = "127.0.0.1",
            Port = 1,
            UnitId = 1,
            StartAddress = 0,
            Value = true,
        };
        var options = new WriteOptions { AllowWrites = false };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteSingleCoil.WriteData(input, options, CancellationToken.None));
        Assert.Contains("AllowWrites = false", ex.Message);
    }

    [Fact]
    public async Task WriteSingleRegister_Throws_When_AllowWrites_False_Without_Opening_Socket()
    {
        var input = new WriteSingleRegisterInput
        {
            Host = "127.0.0.1",
            Port = 1,
            UnitId = 1,
            StartAddress = 0,
            ValueType = ModbusValueType.UInt16,
            Value = (ushort)42,
        };
        var options = new WriteOptions { AllowWrites = false };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteSingleRegister.WriteData(input, options, CancellationToken.None));
        Assert.Contains("AllowWrites = false", ex.Message);
    }
}
