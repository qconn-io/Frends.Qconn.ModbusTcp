using System;
using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Frends.Qconn.ModbusTcp.Write;
using Frends.Qconn.ModbusTcp.Write.Definitions;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Verifies the WriteGuard blocks writes when ModbusWritesAllowed=false before any socket is opened.</summary>
[Collection("WriteGuard")]
public class WriteGuardTests : IDisposable
{
    private readonly string? originalEnv;

    public WriteGuardTests()
    {
        originalEnv = Environment.GetEnvironmentVariable(WriteGuard.EnvVar);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(WriteGuard.EnvVar, originalEnv);
    }

    [Fact]
    public void Unset_EnvVar_Allows_Writes()
    {
        Environment.SetEnvironmentVariable(WriteGuard.EnvVar, null);
        WriteGuard.EnsureAllowed(); // should not throw
    }

    [Fact]
    public void True_EnvVar_Allows_Writes()
    {
        Environment.SetEnvironmentVariable(WriteGuard.EnvVar, "true");
        WriteGuard.EnsureAllowed();
    }

    [Fact]
    public void False_EnvVar_Throws_Before_Socket_Open()
    {
        Environment.SetEnvironmentVariable(WriteGuard.EnvVar, "false");
        var ex = Assert.Throws<InvalidOperationException>(() => WriteGuard.EnsureAllowed());
        Assert.Contains("Modbus writes are disabled", ex.Message);
        Assert.Contains("ModbusWritesAllowed=false", ex.Message);
    }

    [Fact]
    public async Task WriteSingleCoil_Throws_When_Disabled_Without_Opening_Socket()
    {
        Environment.SetEnvironmentVariable(WriteGuard.EnvVar, "false");

        var input = new WriteInput
        {
            // deliberately use a non-routable host — if WriteGuard fails, the connect attempt would hang
            Host = "127.0.0.1",
            Port = 1, // a port guaranteed not to have a Modbus server
            UnitId = 1,
            StartAddress = 0,
            Values = true,
        };
        var options = new WriteOptions();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WriteSingleCoil.WriteData(input, options, CancellationToken.None));
        Assert.Contains("Modbus writes are disabled", ex.Message);
    }

    [Fact]
    public void Numeric_One_Also_Allowed()
    {
        Environment.SetEnvironmentVariable(WriteGuard.EnvVar, "1");
        WriteGuard.EnsureAllowed();
    }
}
