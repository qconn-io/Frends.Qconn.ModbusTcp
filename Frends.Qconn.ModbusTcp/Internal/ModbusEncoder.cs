using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Internal;

/// <summary>Pure stateless encoder: typed values → ushort[] register array.
/// Inverse of ModbusDecoder: applies inverse scale/offset (raw = (value - Offset) / Scale)
/// and produces register words in the requested ByteOrder.</summary>
internal static class ModbusEncoder
{
    /// <summary>Encodes an array of typed values into a flat register array per ValueType/ByteOrder/Scale/Offset.
    /// For AsciiString the caller must pass (string, registerCount) via EncodeAsciiString instead.</summary>
    internal static ushort[] Encode(
        object values,
        ModbusValueType valueType,
        ByteOrder byteOrder,
        double scale,
        double offset)
    {
        if (valueType == ModbusValueType.AsciiString)
            throw new ArgumentException("Use EncodeAsciiString for AsciiString values.", nameof(valueType));

        bool scaleNeeded = scale != 1.0 || offset != 0.0;
        double invScale = scale != 0.0 ? 1.0 / scale : 1.0;
        double InvScale(double v) => (v - offset) * invScale;

        switch (valueType)
        {
            case ModbusValueType.Raw:
                return ToUShortArray(values);

            case ModbusValueType.UInt16:
            {
                var arr = ToUShortArray(values, scaleNeeded ? InvScale : null);
                return arr;
            }

            case ModbusValueType.Int16:
            {
                var arr = ToShortArray(values, scaleNeeded ? InvScale : null);
                var regs = new ushort[arr.Length];
                for (int i = 0; i < arr.Length; i++) regs[i] = unchecked((ushort)arr[i]);
                return regs;
            }

            case ModbusValueType.Int32:
            {
                var arr = ToInt32Array(values, scaleNeeded ? InvScale : null);
                var regs = new ushort[arr.Length * 2];
                for (int i = 0; i < arr.Length; i++)
                {
                    var bytes = BitConverter.GetBytes(arr[i]);
                    var (r0, r1) = BytesToRegisters32(bytes, byteOrder);
                    regs[i * 2] = r0;
                    regs[i * 2 + 1] = r1;
                }
                return regs;
            }

            case ModbusValueType.UInt32:
            {
                var arr = ToUInt32Array(values, scaleNeeded ? InvScale : null);
                var regs = new ushort[arr.Length * 2];
                for (int i = 0; i < arr.Length; i++)
                {
                    var bytes = BitConverter.GetBytes(arr[i]);
                    var (r0, r1) = BytesToRegisters32(bytes, byteOrder);
                    regs[i * 2] = r0;
                    regs[i * 2 + 1] = r1;
                }
                return regs;
            }

            case ModbusValueType.Float32:
            {
                var arr = ToFloat32Array(values, scaleNeeded ? InvScale : null);
                var regs = new ushort[arr.Length * 2];
                for (int i = 0; i < arr.Length; i++)
                {
                    var bytes = BitConverter.GetBytes(arr[i]);
                    var (r0, r1) = BytesToRegisters32(bytes, byteOrder);
                    regs[i * 2] = r0;
                    regs[i * 2 + 1] = r1;
                }
                return regs;
            }

            case ModbusValueType.Float64:
            {
                var arr = ToFloat64Array(values, scaleNeeded ? InvScale : null);
                var regs = new ushort[arr.Length * 4];
                for (int i = 0; i < arr.Length; i++)
                {
                    var bytes = BitConverter.GetBytes(arr[i]);
                    var (r0, r1, r2, r3) = BytesToRegisters64(bytes, byteOrder);
                    regs[i * 4] = r0;
                    regs[i * 4 + 1] = r1;
                    regs[i * 4 + 2] = r2;
                    regs[i * 4 + 3] = r3;
                }
                return regs;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(valueType), valueType, null);
        }
    }

    /// <summary>Encodes an ASCII string into exactly registerCount registers, null-padded on the right if short.
    /// Throws if the string's byte length exceeds registerCount * 2.</summary>
    internal static ushort[] EncodeAsciiString(string value, ushort registerCount)
    {
        int byteLen = registerCount * 2;
        var bytes = new byte[byteLen];
        var encoded = Encoding.ASCII.GetBytes(value ?? string.Empty);
        if (encoded.Length > byteLen)
            throw new ArgumentException($"ASCII string is {encoded.Length} bytes but only {byteLen} bytes fit in {registerCount} registers.");
        Array.Copy(encoded, 0, bytes, 0, encoded.Length);

        var regs = new ushort[registerCount];
        for (int i = 0; i < registerCount; i++)
            regs[i] = (ushort)((bytes[i * 2] << 8) | bytes[i * 2 + 1]);
        return regs;
    }

    // ----- byte↔register splits (inverse of ModbusDecoder.GetBytes32/64) -----

    private static (ushort r0, ushort r1) BytesToRegisters32(byte[] le, ByteOrder order) => order switch
    {
        // Decoder: LE = [r1L, r1H, r0L, r0H]
        ByteOrder.BigEndian => ((ushort)((le[3] << 8) | le[2]), (ushort)((le[1] << 8) | le[0])),
        // Decoder: LE = [r0H, r0L, r1H, r1L]
        ByteOrder.LittleEndian => ((ushort)((le[0] << 8) | le[1]), (ushort)((le[2] << 8) | le[3])),
        // Decoder: LE = [r1H, r1L, r0H, r0L]
        ByteOrder.BigEndianByteSwap => ((ushort)((le[2] << 8) | le[3]), (ushort)((le[0] << 8) | le[1])),
        // Decoder: LE = [r0L, r0H, r1L, r1H]
        ByteOrder.LittleEndianWordSwap => ((ushort)((le[1] << 8) | le[0]), (ushort)((le[3] << 8) | le[2])),
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
    };

    private static (ushort r0, ushort r1, ushort r2, ushort r3) BytesToRegisters64(byte[] le, ByteOrder order) => order switch
    {
        ByteOrder.BigEndian => (
            (ushort)((le[7] << 8) | le[6]),
            (ushort)((le[5] << 8) | le[4]),
            (ushort)((le[3] << 8) | le[2]),
            (ushort)((le[1] << 8) | le[0])),
        ByteOrder.LittleEndian => (
            (ushort)((le[0] << 8) | le[1]),
            (ushort)((le[2] << 8) | le[3]),
            (ushort)((le[4] << 8) | le[5]),
            (ushort)((le[6] << 8) | le[7])),
        ByteOrder.BigEndianByteSwap => (
            (ushort)((le[6] << 8) | le[7]),
            (ushort)((le[4] << 8) | le[5]),
            (ushort)((le[2] << 8) | le[3]),
            (ushort)((le[0] << 8) | le[1])),
        ByteOrder.LittleEndianWordSwap => (
            (ushort)((le[5] << 8) | le[4]),
            (ushort)((le[7] << 8) | le[6]),
            (ushort)((le[1] << 8) | le[0]),
            (ushort)((le[3] << 8) | le[2])),
        _ => throw new ArgumentOutOfRangeException(nameof(order), order, null),
    };

    // ----- type coercion helpers -----

    private static ushort[] ToUShortArray(object values, Func<double, double>? scale = null)
    {
        var doubles = ExtractDoubles(values);
        var regs = new ushort[doubles.Length];
        for (int i = 0; i < doubles.Length; i++)
        {
            double v = scale is null ? doubles[i] : scale(doubles[i]);
            regs[i] = (ushort)Math.Round(v);
        }
        return regs;
    }

    private static short[] ToShortArray(object values, Func<double, double>? scale = null)
    {
        var doubles = ExtractDoubles(values);
        var arr = new short[doubles.Length];
        for (int i = 0; i < doubles.Length; i++)
        {
            double v = scale is null ? doubles[i] : scale(doubles[i]);
            arr[i] = (short)Math.Round(v);
        }
        return arr;
    }

    private static int[] ToInt32Array(object values, Func<double, double>? scale = null)
    {
        var doubles = ExtractDoubles(values);
        var arr = new int[doubles.Length];
        for (int i = 0; i < doubles.Length; i++)
        {
            double v = scale is null ? doubles[i] : scale(doubles[i]);
            arr[i] = (int)Math.Round(v);
        }
        return arr;
    }

    private static uint[] ToUInt32Array(object values, Func<double, double>? scale = null)
    {
        var doubles = ExtractDoubles(values);
        var arr = new uint[doubles.Length];
        for (int i = 0; i < doubles.Length; i++)
        {
            double v = scale is null ? doubles[i] : scale(doubles[i]);
            arr[i] = (uint)Math.Round(v);
        }
        return arr;
    }

    private static float[] ToFloat32Array(object values, Func<double, double>? scale = null)
    {
        var doubles = ExtractDoubles(values);
        var arr = new float[doubles.Length];
        for (int i = 0; i < doubles.Length; i++)
            arr[i] = (float)(scale is null ? doubles[i] : scale(doubles[i]));
        return arr;
    }

    private static double[] ToFloat64Array(object values, Func<double, double>? scale = null)
    {
        var doubles = ExtractDoubles(values);
        if (scale is null) return doubles;
        var arr = new double[doubles.Length];
        for (int i = 0; i < doubles.Length; i++) arr[i] = scale(doubles[i]);
        return arr;
    }

    private static double[] ExtractDoubles(object values) => values switch
    {
        double[] d => d,
        float[] f => f.Select(x => (double)x).ToArray(),
        int[] i => i.Select(x => (double)x).ToArray(),
        uint[] ui => ui.Select(x => (double)x).ToArray(),
        short[] s => s.Select(x => (double)x).ToArray(),
        ushort[] us => us.Select(x => (double)x).ToArray(),
        long[] l => l.Select(x => (double)x).ToArray(),
        System.Collections.IEnumerable ie => ((IEnumerable<object>)CoerceEnumerable(ie)).Select(ToDouble).ToArray(),
        double single => new[] { single },
        float f => new[] { (double)f },
        int i => new[] { (double)i },
        uint ui => new[] { (double)ui },
        short s => new[] { (double)s },
        ushort us => new[] { (double)us },
        long l => new[] { (double)l },
        _ => throw new ArgumentException($"Unsupported Values type for numeric encoding: {values?.GetType().FullName ?? "null"}."),
    };

    private static IEnumerable<object?> CoerceEnumerable(System.Collections.IEnumerable source)
    {
        foreach (var o in source) yield return o;
    }

    private static double ToDouble(object? o) => o switch
    {
        null => throw new ArgumentException("Values array contains null."),
        double d => d,
        float f => f,
        int i => i,
        uint ui => ui,
        short s => s,
        ushort us => us,
        long l => l,
        string str when double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Normalizes bool or bool-array-ish input into bool[].</summary>
    internal static bool[] EncodeBools(object values)
    {
        return values switch
        {
            bool b => new[] { b },
            bool[] arr => arr,
            System.Collections.IEnumerable ie => ((IEnumerable<object>)CoerceEnumerable(ie)).Select(ToBool).ToArray(),
            _ => throw new ArgumentException($"Expected bool or bool[] for coil values, got {values?.GetType().FullName ?? "null"}."),
        };
    }

    private static bool ToBool(object? o) => o switch
    {
        null => throw new ArgumentException("Values array contains null."),
        bool b => b,
        string s => bool.Parse(s),
        _ => Convert.ToBoolean(o, System.Globalization.CultureInfo.InvariantCulture),
    };
}
