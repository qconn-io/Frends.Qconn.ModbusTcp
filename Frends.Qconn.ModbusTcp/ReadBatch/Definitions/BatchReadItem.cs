using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Frends.Qconn.ModbusTcp.Read.Definitions;

namespace Frends.Qconn.ModbusTcp.ReadBatch.Definitions;

/// <summary>Specification for a single read within a batch operation.</summary>
public class BatchReadItem
{
    /// <summary>Name used as the dictionary key in BatchResult.Items. Must be unique within the batch.</summary>
    /// <example>ActivePower</example>
    [DefaultValue("\"\"")]
    [DisplayFormat(DataFormatString = "Text")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Modbus data type to read for this item.</summary>
    /// <example>HoldingRegisters</example>
    [DefaultValue(ModbusDataType.HoldingRegisters)]
    public ModbusDataType DataType { get; set; } = ModbusDataType.HoldingRegisters;

    /// <summary>Starting register address for this item.</summary>
    /// <example>0</example>
    [DefaultValue(0)]
    public ushort StartAddress { get; set; } = 0;

    /// <summary>Number of typed values to read for this item.</summary>
    /// <example>1</example>
    [DefaultValue(1)]
    public ushort NumberOfValues { get; set; } = 1;

    /// <summary>How to interpret the raw register words for this item.</summary>
    /// <example>Float32</example>
    [DefaultValue(ModbusValueType.Raw)]
    public ModbusValueType ValueType { get; set; } = ModbusValueType.Raw;

    /// <summary>Scale factor for this item: output = (raw * Scale) + Offset.</summary>
    /// <example>1.0</example>
    [DefaultValue(1.0)]
    public double Scale { get; set; } = 1.0;

    /// <summary>Offset for this item: output = (raw * Scale) + Offset.</summary>
    /// <example>0</example>
    [DefaultValue("0")]
    [DisplayFormat(DataFormatString = "Expression")]
    public double Offset { get; set; } = 0.0;
}
