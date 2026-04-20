using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Input parameters for writing a single Modbus holding register (FC06).
/// Only 16-bit value types are valid: Raw, UInt16, Int16.</summary>
public class WriteSingleRegisterInput
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

    /// <summary>Holding register address to write. Zero-based by default (see Options.AddressingMode).</summary>
    /// <example>0</example>
    [DefaultValue(0)]
    public ushort StartAddress { get; set; } = 0;

    /// <summary>How to encode the value into a 16-bit register word.
    /// Raw and UInt16 treat the value as an unsigned 16-bit integer (0–65535).
    /// Int16 treats it as a signed 16-bit integer (-32768–32767).
    /// For multi-register types (Float32, Int32, etc.) use WriteMultiple instead.</summary>
    /// <example>UInt16</example>
    [DefaultValue(ModbusValueType.UInt16)]
    public ModbusValueType ValueType { get; set; } = ModbusValueType.UInt16;

    /// <summary>Value to write. Must be a numeric value compatible with the chosen ValueType.</summary>
    /// <example>42</example>
    [DefaultValue(null)]
    public object? Value { get; set; }
}
