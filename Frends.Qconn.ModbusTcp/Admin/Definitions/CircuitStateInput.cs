using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Qconn.ModbusTcp.Admin.Definitions;

/// <summary>Input for circuit-state admin Tasks: identifies the device whose breaker is being inspected or reset.</summary>
public class CircuitStateInput
{
    /// <summary>Target Modbus device host.</summary>
    /// <example>192.168.1.100</example>
    [DefaultValue("\"127.0.0.1\"")]
    [DisplayFormat(DataFormatString = "Text")]
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>TCP port.</summary>
    /// <example>502</example>
    [DefaultValue(502)]
    public int Port { get; set; } = 502;

    /// <summary>Modbus unit ID.</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public byte UnitId { get; set; } = 1;
}
