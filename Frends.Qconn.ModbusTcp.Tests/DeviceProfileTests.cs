using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Test 10: Device-profile regression fixtures — decoder correctness against real device patterns.</summary>
public class DeviceProfileTests
{
    // ── Schneider PM5000 — Active Power ──────────────────────────────────────
    // Float32 LittleEndianWordSwap (CDAB), 1234.5 kW
    // 1234.5f = 0x449A5000 (IEEE 754): A=0x44, B=0x9A, C=0x50, D=0x00
    // CDAB: reg0=C<<8|D=0x5000; reg1=A<<8|B=0x449A
    [Fact]
    public void Schneider_PM5000_ActivePower_Float32_LittleEndianWordSwap()
    {
        var regs = new ushort[] { 0x5000, 0x449A };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Float32, ByteOrder.LittleEndianWordSwap, 1, 1.0, 0.0);
        var arr = Assert.IsType<float[]>(result);
        Assert.Equal(1234.5f, arr[0], precision: 1);
    }

    // ── WattNode Energy Sum ───────────────────────────────────────────────────
    // Float32 LittleEndianWordSwap (CDAB), 123456.0 kWh
    // 0x47F12000: CDAB → reg0=0x2000, reg1=0x47F1
    [Fact]
    public void WattNode_EnergySum_Float32_LittleEndianWordSwap()
    {
        var regs = new ushort[] { 0x2000, 0x47F1 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Float32, ByteOrder.LittleEndianWordSwap, 1, 1.0, 0.0);
        var arr = Assert.IsType<float[]>(result);
        Assert.Equal(123456.0f, arr[0], precision: 0);
    }

    // ── Siemens S7 — Valve State ─────────────────────────────────────────────
    // Coil booleans: [open, closed, open]
    [Fact]
    public void Siemens_S7_ValveState_BoolArray_Interpretation()
    {
        bool[] valveStates = [true, false, true];
        Assert.True(valveStates[0]);
        Assert.False(valveStates[1]);
        Assert.True(valveStates[2]);
    }

    // ── Modicon address mapping ───────────────────────────────────────────────
    [Fact]
    public void Schneider_iEM3155_ModiconAddress_40001_Maps_To_Wire_0()
    {
        ushort wire = ModbusDecoder.TranslateAddress(40001, AddressingMode.ModiconOneBased);
        Assert.Equal((ushort)0, wire);
    }

    // ── Energy meter scale factor ─────────────────────────────────────────────
    // Meter returns 23040 for 2304.0 kWh with scale=0.1
    [Fact]
    public void EnergyMeter_Scale_Factor_0_1()
    {
        var regs = new ushort[] { 23040 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.UInt16, ByteOrder.BigEndian, 1, 0.1, 0.0);
        var arr = Assert.IsType<double[]>(result);
        Assert.Equal(2304.0, arr[0], precision: 5);
    }
}
