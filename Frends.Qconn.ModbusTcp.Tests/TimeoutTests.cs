using System.Threading;
using System.Threading.Tasks;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using ReadTask = Frends.Qconn.ModbusTcp.Read.Read;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Test 7: Connect timeout — Timeout category, timing within acceptable range.</summary>
[Trait("Category", "Slow")]
public class TimeoutTests
{
    [Fact]
    public async Task ConnectTimeout_BlackHole_Returns_Timeout_Error()
    {
        // 10.255.255.1 is a non-routable black-hole address — packets are silently dropped.
        var input = new Input
        {
            Host = "10.255.255.1",
            Port = 502,
            UnitId = 1,
            DataType = ModbusDataType.HoldingRegisters,
            StartAddress = 0,
            NumberOfValues = 1,
            ValueType = ModbusValueType.Raw,
        };
        var options = new Options { ConnectTimeoutMs = 500 };

        var result = await ReadTask.ReadData(input, options, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCategory.Timeout, result.Error!.Category);
        Assert.True(result.Error.IsTransient);
        Assert.InRange(result.Diagnostics.ConnectTimeMs, 450, 700);
    }
}
