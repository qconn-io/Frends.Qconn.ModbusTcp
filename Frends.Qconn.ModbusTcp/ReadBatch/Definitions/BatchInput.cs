using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Qconn.ModbusTcp.ReadBatch.Definitions;

/// <summary>Input for a batched Modbus TCP read operation over a single TCP connection.</summary>
public class BatchInput
{
    /// <summary>IP address or hostname of the Modbus TCP slave device.</summary>
    /// <example>192.168.1.100</example>
    [DefaultValue("\"127.0.0.1\"")]
    [DisplayFormat(DataFormatString = "Text")]
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>TCP port of the Modbus TCP slave. Standard Modbus port is 502.</summary>
    /// <example>502</example>
    [DefaultValue(502)]
    public int Port { get; set; } = 502;

    /// <summary>Modbus unit ID (slave address). Most devices use 1.</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public byte UnitId { get; set; } = 1;

    /// <summary>List of register reads to execute over the single TCP connection.
    /// IMPORTANT: A Modbus exception on one item does not abort the batch — only socket-level failures do.
    /// Each item reports its own success/error in BatchResult.Items.</summary>
    /// <example>[{ "Name": "Power", "DataType": "HoldingRegisters", "StartAddress": 0, "NumberOfValues": 1, "ValueType": "Float32" }]</example>
    public List<BatchReadItem> Items { get; set; } = [];
}
