using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Input for WriteBatch: a shared connection and a list of write items to apply in order.</summary>
public class WriteBatchInput
{
    /// <summary>Target Modbus device host.</summary>
    /// <example>192.168.1.100</example>
    [DefaultValue("\"127.0.0.1\"")]
    [DisplayFormat(DataFormatString = "Text")]
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>TCP port.</summary>
    [DefaultValue(502)]
    public int Port { get; set; } = 502;

    /// <summary>Modbus unit ID.</summary>
    [DefaultValue(1)]
    public byte UnitId { get; set; } = 1;

    /// <summary>Items to write in sequence. Each item is applied independently; per-item failures are recorded but do not
    /// abort the batch unless the failure is socket-level.</summary>
    public List<WriteBatchItem> Items { get; set; } = new();
}
