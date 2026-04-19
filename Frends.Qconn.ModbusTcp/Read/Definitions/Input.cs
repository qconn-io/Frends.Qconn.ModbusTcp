using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Qconn.ModbusTcp.Read.Definitions;

/// <summary>Parameters for a single Modbus TCP read operation.</summary>
public class Input
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

    /// <summary>Modbus unit ID (slave address). Most devices use 1.
    /// Modbus/TCP-to-RTU gateways often use 247 or 255.</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public byte UnitId { get; set; } = 1;

    /// <summary>Modbus data type to read. Determines which function code is used:
    /// Coils (FC01), DiscreteInputs (FC02), HoldingRegisters (FC03), InputRegisters (FC04).</summary>
    /// <example>HoldingRegisters</example>
    [DefaultValue(ModbusDataType.HoldingRegisters)]
    public ModbusDataType DataType { get; set; } = ModbusDataType.HoldingRegisters;

    /// <summary>Starting register address. For zero-based addressing (default) this is the raw PDU address.
    /// For Modicon one-based addressing, use the address as shown in your device manual (e.g. 40001).</summary>
    /// <example>0</example>
    [DefaultValue(0)]
    public ushort StartAddress { get; set; } = 0;

    /// <summary>Number of typed values to read. The Task automatically converts this to the correct
    /// register count: Float32 = 2 registers each, Float64 = 4 registers each, others = 1 register each.
    /// For AsciiString, this is the number of registers (2 ASCII characters per register).</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public ushort NumberOfValues { get; set; } = 1;

    /// <summary>How to interpret the raw 16-bit register words. Ignored for Coils and DiscreteInputs
    /// (those always return bool[]). Raw returns ushort[] unchanged.</summary>
    /// <example>Float32</example>
    [DefaultValue(ModbusValueType.Raw)]
    [UIHint(nameof(DataType), "", ModbusDataType.HoldingRegisters, ModbusDataType.InputRegisters)]
    public ModbusValueType ValueType { get; set; } = ModbusValueType.Raw;
}
