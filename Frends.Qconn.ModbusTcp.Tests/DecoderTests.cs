using System;
using Frends.Qconn.ModbusTcp.Internal;
using Frends.Qconn.ModbusTcp.Read.Definitions;
using Xunit;

namespace Frends.Qconn.ModbusTcp.Tests;

/// <summary>Tests 2, 3, 4, 5: AddressingMode translation, decoder correctness, scale/offset, AsciiString.</summary>
public class DecoderTests
{
    // ─── Test 2: AddressingMode translation ───────────────────────────────────

    [Fact]
    public void Modicon_40001_Translates_To_WireAddress_0()
    {
        ushort wire = ModbusDecoder.TranslateAddress(40001, AddressingMode.ModiconOneBased);
        Assert.Equal((ushort)0, wire);
    }

    [Fact]
    public void Modicon_40010_Translates_To_WireAddress_9()
    {
        ushort wire = ModbusDecoder.TranslateAddress(40010, AddressingMode.ModiconOneBased);
        Assert.Equal((ushort)9, wire);
    }

    [Fact]
    public void ZeroBased_1_Returns_WireAddress_1()
    {
        ushort wire = ModbusDecoder.TranslateAddress(1, AddressingMode.ZeroBased);
        Assert.Equal((ushort)1, wire);
    }

    [Fact]
    public void ZeroBased_0_Returns_WireAddress_0()
    {
        ushort wire = ModbusDecoder.TranslateAddress(0, AddressingMode.ZeroBased);
        Assert.Equal((ushort)0, wire);
    }

    [Fact]
    public void Modicon_30001_Translates_To_WireAddress_0()
    {
        ushort wire = ModbusDecoder.TranslateAddress(30001, AddressingMode.ModiconOneBased);
        Assert.Equal((ushort)0, wire);
    }

    [Fact]
    public void Modicon_1_Translates_To_WireAddress_0()
    {
        ushort wire = ModbusDecoder.TranslateAddress(1, AddressingMode.ModiconOneBased);
        Assert.Equal((ushort)0, wire);
    }

    // ─── Test 3: Float32 decoder correctness — all 4 byte orders ─────────────
    // Reference fixture: 123456.0f = 0x47F12000 (IEEE-754)
    // BigEndian registers  = {0x47F1, 0x2000}
    // LittleEndian regs    = {0x0020, 0xF147}  (DCBA: D=0x00,C=0x20,B=0xF1,A=0x47)
    // BigEndianByteSwap    = {0xF147, 0x0020}  (BADC: B=0xF1,A=0x47,D=0x00,C=0x20)
    // LittleEndianWordSwap = {0x2000, 0x47F1}  (CDAB: C=0x20,D=0x00,A=0x47,B=0xF1)

    [Theory]
    [InlineData(ByteOrder.BigEndian, 0x47F1, 0x2000)]
    [InlineData(ByteOrder.LittleEndian, 0x0020, 0xF147)]
    [InlineData(ByteOrder.BigEndianByteSwap, 0xF147, 0x0020)]
    [InlineData(ByteOrder.LittleEndianWordSwap, 0x2000, 0x47F1)]
    public void Float32_AllByteOrders_Decode_To_123456(ByteOrder order, ushort r0, ushort r1)
    {
        var regs = new ushort[] { r0, r1 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Float32, order, 1, 1.0, 0.0);
        var arr = Assert.IsType<float[]>(result);
        Assert.Single(arr);
        Assert.Equal(123456.0f, arr[0], precision: 0);
    }

    [Fact]
    public void Int32_BigEndian_Decodes_Correctly()
    {
        // 0x000186A0 = 100000
        var regs = new ushort[] { 0x0001, 0x86A0 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Int32, ByteOrder.BigEndian, 1, 1.0, 0.0);
        var arr = Assert.IsType<int[]>(result);
        Assert.Equal(100000, arr[0]);
    }

    [Fact]
    public void Int32_LittleEndianWordSwap_Decodes_Correctly()
    {
        // 0x000186A0: LittleEndianWordSwap (CDAB) → reg0=C,D=0x0086 reg1=A,B=0x00,0x01... wait
        // Let me use CDAB order. A=0x00,B=0x01,C=0x86,D=0xA0
        // LittleEndianWordSwap: reg0=C<<8|D=0x86A0, reg1=A<<8|B=0x0001
        var regs = new ushort[] { 0x86A0, 0x0001 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Int32, ByteOrder.LittleEndianWordSwap, 1, 1.0, 0.0);
        var arr = Assert.IsType<int[]>(result);
        Assert.Equal(100000, arr[0]);
    }

    [Fact]
    public void UInt32_BigEndian_Decodes_Correctly()
    {
        // 0xFFFF0000 = 4294901760
        var regs = new ushort[] { 0xFFFF, 0x0000 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.UInt32, ByteOrder.BigEndian, 1, 1.0, 0.0);
        var arr = Assert.IsType<uint[]>(result);
        Assert.Equal(4294901760u, arr[0]);
    }

    [Fact]
    public void Float64_BigEndian_Decodes_Known_Value()
    {
        // 1.0 as double = 0x3FF0000000000000
        var regs = new ushort[] { 0x3FF0, 0x0000, 0x0000, 0x0000 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Float64, ByteOrder.BigEndian, 1, 1.0, 0.0);
        var arr = Assert.IsType<double[]>(result);
        Assert.Equal(1.0, arr[0], precision: 10);
    }

    [Fact]
    public void Int16_BigEndian_Decodes_Correctly()
    {
        // -1 as int16 = 0xFFFF
        var regs = new ushort[] { 0xFFFF };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Int16, ByteOrder.BigEndian, 1, 1.0, 0.0);
        var arr = Assert.IsType<short[]>(result);
        Assert.Equal((short)-1, arr[0]);
    }

    [Fact]
    public void Raw_Returns_Ushort_Array_Unchanged()
    {
        var regs = new ushort[] { 0x1234, 0x5678 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Raw, ByteOrder.BigEndian, 2, 1.0, 0.0);
        var arr = Assert.IsType<ushort[]>(result);
        Assert.Equal(regs, arr);
    }

    // ─── Test 4: Scale / Offset ───────────────────────────────────────────────

    [Fact]
    public void Scale_01_Applied_To_Int16_2304_Returns_230_4()
    {
        var regs = new ushort[] { 2304 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Int16, ByteOrder.BigEndian, 1, 0.1, 0.0);
        var arr = Assert.IsType<double[]>(result);
        Assert.Equal(230.4, arr[0], precision: 5);
    }

    [Fact]
    public void Scale_And_Offset_Applied_Correctly()
    {
        // raw=100, scale=0.5, offset=10 → 100*0.5+10 = 60
        var regs = new ushort[] { 100 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.UInt16, ByteOrder.BigEndian, 1, 0.5, 10.0);
        var arr = Assert.IsType<double[]>(result);
        Assert.Equal(60.0, arr[0], precision: 10);
    }

    [Fact]
    public void No_Scale_Int16_Returns_Short_Array()
    {
        var regs = new ushort[] { 42 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Int16, ByteOrder.BigEndian, 1, 1.0, 0.0);
        Assert.IsType<short[]>(result);
    }

    [Fact]
    public void Float32_With_Scale_Returns_Double_Array()
    {
        var regs = new ushort[] { 0x3F80, 0x0000 }; // 1.0f in BigEndian
        var result = ModbusDecoder.Decode(regs, ModbusValueType.Float32, ByteOrder.BigEndian, 1, 2.0, 0.0);
        var arr = Assert.IsType<double[]>(result);
        Assert.Equal(2.0, arr[0], precision: 5);
    }

    // ─── Test 5: AsciiString ──────────────────────────────────────────────────

    [Fact]
    public void AsciiString_NullPadded_Returns_Trimmed()
    {
        // "Hello" = 0x48 65 6C 6C 6F then null
        // reg0=0x4865('H','e'), reg1=0x6C6C('l','l'), reg2=0x6F00('o','\0')
        var regs = new ushort[] { 0x4865, 0x6C6C, 0x6F00 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.AsciiString, ByteOrder.BigEndian, 3, 1.0, 0.0);
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void AsciiString_FullRegisters_No_Null_Returns_Full_String()
    {
        // "AB" = 0x41 0x42
        var regs = new ushort[] { 0x4142 };
        var result = ModbusDecoder.Decode(regs, ModbusValueType.AsciiString, ByteOrder.BigEndian, 1, 1.0, 0.0);
        Assert.Equal("AB", result);
    }

    // ─── Register count computation ───────────────────────────────────────────

    [Theory]
    [InlineData(ModbusValueType.Raw, 5, 5)]
    [InlineData(ModbusValueType.Int16, 5, 5)]
    [InlineData(ModbusValueType.UInt16, 5, 5)]
    [InlineData(ModbusValueType.Int32, 5, 10)]
    [InlineData(ModbusValueType.UInt32, 5, 10)]
    [InlineData(ModbusValueType.Float32, 5, 10)]
    [InlineData(ModbusValueType.Float64, 5, 20)]
    [InlineData(ModbusValueType.AsciiString, 5, 5)]
    public void ComputeRegisterCount_Returns_Correct_Count(ModbusValueType vt, ushort n, ushort expected)
    {
        Assert.Equal(expected, ModbusDecoder.ComputeRegisterCount(vt, n));
    }
}
