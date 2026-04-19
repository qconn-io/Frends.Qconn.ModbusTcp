using System;
using System.Text;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Pure stateless decoder: ushort[] → typed values with endianness and scale/offset.</summary>
internal static class ModbusDecoder
{
    /// <summary>Translates a user-supplied StartAddress to the wire address per AddressingMode.</summary>
    internal static ushort TranslateAddress(ushort address, AddressingMode mode)
    {
        if (mode == AddressingMode.ZeroBased) return address;
        // ModiconOneBased: strip leading range prefix and subtract 1
        if (address >= 40001) return (ushort)(address - 40001);
        if (address >= 30001) return (ushort)(address - 30001);
        if (address >= 10001) return (ushort)(address - 10001);
        if (address >= 1)     return (ushort)(address - 1);
        return address;
    }

    /// <summary>Returns the number of 16-bit registers needed on the wire for the given value type and count.
    /// For AsciiString, numberOfValues IS the register count (spec: NumberOfValues interpreted as registers).</summary>
    internal static ushort ComputeRegisterCount(ModbusValueType valueType, ushort numberOfValues)
    {
        return valueType switch
        {
            ModbusValueType.Int32 or ModbusValueType.UInt32 or ModbusValueType.Float32
                => (ushort)(numberOfValues * 2),
            ModbusValueType.Float64
                => (ushort)(numberOfValues * 4),
            _ => numberOfValues,
        };
    }

    /// <summary>Decodes a ushort[] register array into a typed value array (or string), applying scale and offset.</summary>
    internal static object Decode(
        ushort[] registers,
        ModbusValueType valueType,
        ByteOrder byteOrder,
        int numberOfValues,
        double scale,
        double offset)
    {
        bool scaleNeeded = scale != 1.0 || offset != 0.0;

        switch (valueType)
        {
            case ModbusValueType.Raw:
                return registers;

            case ModbusValueType.Int16:
            {
                var result = new short[numberOfValues];
                for (int i = 0; i < numberOfValues; i++)
                    result[i] = (short)registers[i];
                if (!scaleNeeded) return result;
                return ApplyScale(result, scale, offset);
            }

            case ModbusValueType.UInt16:
            {
                if (!scaleNeeded) return registers;
                return ApplyScale(registers, scale, offset);
            }

            case ModbusValueType.Int32:
            {
                var result = new int[numberOfValues];
                for (int i = 0; i < numberOfValues; i++)
                    result[i] = BitConverter.ToInt32(GetBytes32(registers[i * 2], registers[i * 2 + 1], byteOrder), 0);
                if (!scaleNeeded) return result;
                return ApplyScale(result, scale, offset);
            }

            case ModbusValueType.UInt32:
            {
                var result = new uint[numberOfValues];
                for (int i = 0; i < numberOfValues; i++)
                    result[i] = BitConverter.ToUInt32(GetBytes32(registers[i * 2], registers[i * 2 + 1], byteOrder), 0);
                if (!scaleNeeded) return result;
                return ApplyScale(result, scale, offset);
            }

            case ModbusValueType.Float32:
            {
                var result = new float[numberOfValues];
                for (int i = 0; i < numberOfValues; i++)
                    result[i] = BitConverter.ToSingle(GetBytes32(registers[i * 2], registers[i * 2 + 1], byteOrder), 0);
                if (!scaleNeeded) return result;
                return ApplyScale(result, scale, offset);
            }

            case ModbusValueType.Float64:
            {
                var result = new double[numberOfValues];
                for (int i = 0; i < numberOfValues; i++)
                {
                    double raw = BitConverter.ToDouble(
                        GetBytes64(registers[i * 4], registers[i * 4 + 1], registers[i * 4 + 2], registers[i * 4 + 3], byteOrder), 0);
                    result[i] = raw * scale + offset;
                }
                return result;
            }

            case ModbusValueType.AsciiString:
            {
                var bytes = new byte[registers.Length * 2];
                for (int i = 0; i < registers.Length; i++)
                {
                    bytes[i * 2]     = (byte)(registers[i] >> 8);
                    bytes[i * 2 + 1] = (byte)(registers[i] & 0xFF);
                }
                return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(valueType), valueType, null);
        }
    }

    // Produces 4 little-endian bytes for BitConverter from two Modbus registers.
    // ByteOrder defines what bytes the registers contain:
    //   BigEndian (ABCD):            reg0=A,B  reg1=C,D  → LE[D,C,B,A]
    //   LittleEndian (DCBA):         reg0=D,C  reg1=B,A  → LE[D,C,B,A] = [r0H,r0L,r1H,r1L]
    //   BigEndianByteSwap (BADC):    reg0=B,A  reg1=D,C  → LE[D,C,B,A] = [r1H,r1L,r0H,r0L]
    //   LittleEndianWordSwap (CDAB): reg0=C,D  reg1=A,B  → LE[D,C,B,A] = [r0L,r0H,r1L,r1H]
    private static byte[] GetBytes32(ushort r0, ushort r1, ByteOrder order) => order switch
    {
        ByteOrder.BigEndian            => [(byte)(r1 & 0xFF), (byte)(r1 >> 8), (byte)(r0 & 0xFF), (byte)(r0 >> 8)],
        ByteOrder.LittleEndian         => [(byte)(r0 >> 8),   (byte)(r0 & 0xFF), (byte)(r1 >> 8), (byte)(r1 & 0xFF)],
        ByteOrder.BigEndianByteSwap    => [(byte)(r1 >> 8),   (byte)(r1 & 0xFF), (byte)(r0 >> 8), (byte)(r0 & 0xFF)],
        ByteOrder.LittleEndianWordSwap => [(byte)(r0 & 0xFF), (byte)(r0 >> 8),   (byte)(r1 & 0xFF), (byte)(r1 >> 8)],
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
    };

    // Produces 8 little-endian bytes for BitConverter from four Modbus registers (Float64).
    // ABCDEFGH = 8 bytes of the double (A=MSB, H=LSB). LE for BitConverter: [H,G,F,E,D,C,B,A].
    //   BigEndian:            reg0=A,B  reg1=C,D  reg2=E,F  reg3=G,H
    //   LittleEndian:         reg0=H,G  reg1=F,E  reg2=D,C  reg3=B,A
    //   BigEndianByteSwap:    reg0=B,A  reg1=D,C  reg2=F,E  reg3=H,G
    //   LittleEndianWordSwap: reg0=C,D  reg1=A,B  reg2=G,H  reg3=E,F
    private static byte[] GetBytes64(ushort r0, ushort r1, ushort r2, ushort r3, ByteOrder order) => order switch
    {
        ByteOrder.BigEndian => [
            (byte)(r3 & 0xFF), (byte)(r3 >> 8),
            (byte)(r2 & 0xFF), (byte)(r2 >> 8),
            (byte)(r1 & 0xFF), (byte)(r1 >> 8),
            (byte)(r0 & 0xFF), (byte)(r0 >> 8),
        ],
        ByteOrder.LittleEndian => [
            (byte)(r0 >> 8),   (byte)(r0 & 0xFF),
            (byte)(r1 >> 8),   (byte)(r1 & 0xFF),
            (byte)(r2 >> 8),   (byte)(r2 & 0xFF),
            (byte)(r3 >> 8),   (byte)(r3 & 0xFF),
        ],
        ByteOrder.BigEndianByteSwap => [
            (byte)(r3 >> 8),   (byte)(r3 & 0xFF),
            (byte)(r2 >> 8),   (byte)(r2 & 0xFF),
            (byte)(r1 >> 8),   (byte)(r1 & 0xFF),
            (byte)(r0 >> 8),   (byte)(r0 & 0xFF),
        ],
        ByteOrder.LittleEndianWordSwap => [
            (byte)(r2 & 0xFF), (byte)(r2 >> 8),
            (byte)(r3 & 0xFF), (byte)(r3 >> 8),
            (byte)(r0 & 0xFF), (byte)(r0 >> 8),
            (byte)(r1 & 0xFF), (byte)(r1 >> 8),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
    };

    private static double[] ApplyScale(short[] values, double scale, double offset)
    {
        var d = new double[values.Length];
        for (int i = 0; i < values.Length; i++) d[i] = values[i] * scale + offset;
        return d;
    }

    private static double[] ApplyScale(ushort[] values, double scale, double offset)
    {
        var d = new double[values.Length];
        for (int i = 0; i < values.Length; i++) d[i] = values[i] * scale + offset;
        return d;
    }

    private static double[] ApplyScale(int[] values, double scale, double offset)
    {
        var d = new double[values.Length];
        for (int i = 0; i < values.Length; i++) d[i] = values[i] * scale + offset;
        return d;
    }

    private static double[] ApplyScale(uint[] values, double scale, double offset)
    {
        var d = new double[values.Length];
        for (int i = 0; i < values.Length; i++) d[i] = values[i] * scale + offset;
        return d;
    }

    private static double[] ApplyScale(float[] values, double scale, double offset)
    {
        var d = new double[values.Length];
        for (int i = 0; i < values.Length; i++) d[i] = values[i] * scale + offset;
        return d;
    }
}
