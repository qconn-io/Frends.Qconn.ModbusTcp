namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Result of a Modbus TCP read operation.</summary>
public class Result
{
    /// <summary>True if the read completed without error and decoding succeeded.</summary>
    public bool Success { get; }

    /// <summary>
    /// Decoded data. Shape depends on Input.DataType and Input.ValueType:
    ///   Coils / DiscreteInputs           → bool[]
    ///   Registers + Raw                  → ushort[]
    ///   Registers + Int16                → short[] (or double[] if Scale/Offset applied)
    ///   Registers + UInt16               → ushort[] (or double[] if Scale/Offset applied)
    ///   Registers + Int32 / UInt32       → int[] or uint[] (or double[] if Scale/Offset applied)
    ///   Registers + Float32              → float[] (or double[] if Scale/Offset applied)
    ///   Registers + Float64              → double[]
    ///   Registers + AsciiString          → string
    /// Null when Success is false.
    /// </summary>
    public object? Data { get; }

    /// <summary>Convenience accessor: Data cast and first element returned when Data is a non-empty array,
    /// or Data itself when Data is a string. Null when Success is false or Data is empty.
    /// Useful for the common single-value read case.</summary>
    public object? FirstValue { get; }

    /// <summary>Raw 16-bit register values as received from the wire, before typing and scaling.
    /// Populated for all register reads regardless of ValueType, to aid debugging.
    /// Null for Coil and DiscreteInput reads.</summary>
    public ushort[]? RawRegisters { get; }

    /// <summary>Structured error information when Success is false; null when Success is true.</summary>
    public ErrorDetail? Error { get; }

    /// <summary>Timing and connection metrics. Always populated, including on failure.</summary>
    public Diagnostics Diagnostics { get; }

    /// <summary>Initializes a successful Result.</summary>
    public Result(object data, ushort[]? rawRegisters, Diagnostics diagnostics)
    {
        Success = true;
        Data = data;
        RawRegisters = rawRegisters;
        Diagnostics = diagnostics;
        Error = null;
        FirstValue = ComputeFirstValue(data);
    }

    /// <summary>Initializes a failed Result.</summary>
    public Result(ErrorDetail error, Diagnostics diagnostics)
    {
        Success = false;
        Data = null;
        FirstValue = null;
        RawRegisters = null;
        Error = error;
        Diagnostics = diagnostics;
    }

    private static object? ComputeFirstValue(object data)
    {
        if (data is string s) return s;
        if (data is bool[] bools && bools.Length > 0) return bools[0];
        if (data is ushort[] ushorts && ushorts.Length > 0) return ushorts[0];
        if (data is short[] shorts && shorts.Length > 0) return shorts[0];
        if (data is int[] ints && ints.Length > 0) return ints[0];
        if (data is uint[] uints && uints.Length > 0) return uints[0];
        if (data is float[] floats && floats.Length > 0) return floats[0];
        if (data is double[] doubles && doubles.Length > 0) return doubles[0];
        return null;
    }
}
