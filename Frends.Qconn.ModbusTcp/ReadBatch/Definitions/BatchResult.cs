using System.Collections.Generic;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.ReadBatch.Definitions;

/// <summary>Result of a single read item within a batch operation.</summary>
public class ReadOutcome
{
    /// <summary>True if this individual item was read and decoded successfully.</summary>
    public bool Success { get; }

    /// <summary>Decoded data for this item. Shape depends on DataType and ValueType. Null on failure.</summary>
    public object? Data { get; }

    /// <summary>First element of Data, or Data itself for strings. Null on failure or empty data.</summary>
    public object? FirstValue { get; }

    /// <summary>Raw 16-bit register values from the wire before typing/scaling. Null for coil reads or on failure.</summary>
    public ushort[]? RawRegisters { get; }

    /// <summary>Error details when Success is false; null when Success is true.</summary>
    public ErrorDetail? Error { get; }

    /// <summary>Initializes a successful ReadOutcome.</summary>
    public ReadOutcome(object data, ushort[]? rawRegisters)
    {
        Success = true;
        Data = data;
        RawRegisters = rawRegisters;
        Error = null;
        FirstValue = ComputeFirstValue(data);
    }

    /// <summary>Initializes a failed ReadOutcome.</summary>
    public ReadOutcome(ErrorDetail error)
    {
        Success = false;
        Data = null;
        FirstValue = null;
        RawRegisters = null;
        Error = error;
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

/// <summary>Result of a batched Modbus TCP read operation.</summary>
public class BatchResult
{
    /// <summary>True unless a socket-level failure aborted the entire batch.
    /// Individual item Modbus exceptions do NOT set this to false — check each item in Items.</summary>
    public bool Success { get; }

    /// <summary>Per-item results keyed by BatchReadItem.Name. Contains outcomes for all items
    /// that were attempted before any socket-level failure.</summary>
    public Dictionary<string, ReadOutcome> Items { get; }

    /// <summary>Batch-level diagnostics (connect time, total time, etc.).</summary>
    public Diagnostics Diagnostics { get; }

    /// <summary>Set only when a socket-level failure aborted the batch early. Null on full success or per-item failures.</summary>
    public ErrorDetail? Error { get; }

    /// <summary>Initializes a successful (or partially-failed) BatchResult.</summary>
    public BatchResult(Dictionary<string, ReadOutcome> items, Diagnostics diagnostics)
    {
        Success = true;
        Items = items;
        Diagnostics = diagnostics;
        Error = null;
    }

    /// <summary>Initializes a BatchResult aborted by a socket-level failure.</summary>
    public BatchResult(Dictionary<string, ReadOutcome> items, Diagnostics diagnostics, ErrorDetail error)
    {
        Success = false;
        Items = items;
        Diagnostics = diagnostics;
        Error = error;
    }
}
