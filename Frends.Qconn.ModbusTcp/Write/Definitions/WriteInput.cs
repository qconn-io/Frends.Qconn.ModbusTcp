using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.Write.Definitions;

/// <summary>Input parameters for Modbus TCP write operations.</summary>
public class WriteInput
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

    /// <summary>Modbus data type to write. Determines which function code is used:
    /// Coils (FC05/FC15), HoldingRegisters (FC06/FC16).</summary>
    /// <example>HoldingRegisters</example>
    [DefaultValue(ModbusDataType.HoldingRegisters)]
    public ModbusDataType DataType { get; set; } = ModbusDataType.HoldingRegisters;

    /// <summary>Starting register or coil address. For zero-based addressing (default) this is
    /// the raw PDU address. For Modicon one-based, use the address as shown in your device manual.</summary>
    /// <example>0</example>
    [DefaultValue(0)]
    public ushort StartAddress { get; set; } = 0;

    /// <summary>Number of typed values to write. The Task automatically converts this to the correct
    /// register count: Float32 = 2 registers each, Float64 = 4 registers each, others = 1 each.
    /// For AsciiString, this is the number of registers (2 ASCII characters per register).
    /// Used by WriteMultiple and WriteBatch; ignored by WriteSingleCoil and WriteSingleRegister.</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public ushort NumberOfValues { get; set; } = 1;

    /// <summary>How to encode the Values payload into 16-bit Modbus registers.
    /// Ignored for Coil writes (those always encode as bool).</summary>
    /// <example>UInt16</example>
    [DefaultValue(ModbusValueType.UInt16)]
    [UIHint(nameof(DataType), "", ModbusDataType.HoldingRegisters)]
    public ModbusValueType ValueType { get; set; } = ModbusValueType.UInt16;

    /// <summary>Values to write. Shape depends on the Write Task:
    /// WriteSingleCoil: bool.
    /// WriteSingleRegister: one numeric value of the chosen ValueType.
    /// WriteMultiple against coils: bool[].
    /// WriteMultiple against registers: numeric array of the chosen ValueType (or a string for AsciiString).
    /// </summary>
    /// <example>42</example>
    [DefaultValue(null)]
    public object? Values { get; set; }
}
