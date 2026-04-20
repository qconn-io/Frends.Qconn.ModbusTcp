using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Input parameters for writing a single Modbus coil (FC05).</summary>
public class WriteSingleCoilInput
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

    /// <summary>Coil address to write. Zero-based by default (see Options.AddressingMode).</summary>
    /// <example>0</example>
    [DefaultValue(0)]
    public ushort StartAddress { get; set; } = 0;

    /// <summary>Coil value to write. True = ON, False = OFF.</summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool Value { get; set; } = true;
}
