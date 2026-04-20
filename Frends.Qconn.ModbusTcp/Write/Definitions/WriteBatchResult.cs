using System.Collections.Generic;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Per-item outcome within a WriteBatchResult.</summary>
public class WriteOutcome
{
    /// <summary>True if the write succeeded.</summary>
    public bool Success { get; }

    /// <summary>Number of 16-bit registers / coils written on the wire. Zero on failure.</summary>
    public ushort WireRegistersWritten { get; }

    /// <summary>Per-item error detail (null on success).</summary>
    public ErrorDetail? Error { get; }

    public WriteOutcome(ushort wireRegistersWritten)
    {
        Success = true;
        WireRegistersWritten = wireRegistersWritten;
        Error = null;
    }

    public WriteOutcome(ErrorDetail error)
    {
        Success = false;
        WireRegistersWritten = 0;
        Error = error;
    }
}

/// <summary>Result of a WriteBatch operation.</summary>
public class WriteBatchResult
{
    /// <summary>False if a socket-level failure aborted the batch; true otherwise. Per-item Modbus exceptions
    /// do not affect this flag — inspect Items[name].Success for item-level outcomes.</summary>
    public bool Success { get; }

    /// <summary>Per-item outcomes keyed by WriteBatchItem.Name.</summary>
    public Dictionary<string, WriteOutcome> Items { get; }

    /// <summary>Batch-level timing.</summary>
    public Diagnostics Diagnostics { get; }

    /// <summary>Set only when a socket-level failure aborted the batch; null otherwise.</summary>
    public ErrorDetail? Error { get; }

    public WriteBatchResult(Dictionary<string, WriteOutcome> items, Diagnostics diagnostics)
    {
        Success = true;
        Items = items;
        Diagnostics = diagnostics;
        Error = null;
    }

    public WriteBatchResult(Dictionary<string, WriteOutcome> items, Diagnostics diagnostics, ErrorDetail error)
    {
        Success = false;
        Items = items;
        Diagnostics = diagnostics;
        Error = error;
    }
}
